using System.Diagnostics;
using System.Globalization;
using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Frame rate over the last second, and smoothed per-frame costs.
public sealed record RenderStats(double Fps, double RenderMs, double PushMs);

/// Renders the current theme at its frame rate and sends each frame to the device. Once started, rendering and sending
/// run on two threads, so a frame is drawn while the previous one is still going to the screen: the frame time is the
/// longer of the two, not their sum, which leaves room for CPU-heavy shader backgrounds. IDisplayDevice is
/// single-threaded, so only the sending thread opens, pushes to, reconnects and closes it. The caller still disposes the
/// device. PushFrame is synchronous (about 21 ms on the Universal Screen), so the device usually sets the ceiling
/// (about 47 fps). A frame the sender hasn't picked up when the next one is ready is dropped: the screen always gets the
/// newest.
public sealed class RenderLoop : IDisposable
{
    public static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);
    static readonly TimeSpan CapCheckDelay = TimeSpan.FromSeconds(3);
    static readonly TimeSpan StatsWindow = TimeSpan.FromSeconds(1);

    /// A rendered frame on its way to the screen, with what the sender needs to know about how it was made.
    sealed record Frame(SKImage Image, double RenderMs, int TargetFps, TimeSpan ThemeStart);

    readonly IDisplayDevice _device;
    readonly SensorStore _sensors;
    readonly ILog _log;
    readonly LConnectDetector _lconnect;
    readonly Queue<TimeSpan> _frameTimes = new();
    readonly AutoResetEvent _frameReady = new(false);

    // Runs only while the loop does, so a Start after Stop (a host resuming after a pause) carries on where it left off,
    // and every time measured on it (the stats window, the reconnect delay, the theme's start) still makes sense.
    readonly Stopwatch _clock = new();
    volatile object? _fpsOverride; // a boxed int, or null: set from any thread, read by the render thread

    // Rendering side (the render thread, or the Step caller).
    LoadedTheme? _pending;
    LoadedTheme? _theme;
    ThemeRenderer? _renderer;
    SKSurface? _fitSurface;
    TimeSpan _themeStart;

    // Sending side (the sending thread, or the Step caller): the device and the stats.
    bool _deviceOpen;
    TimeSpan _nextOpenAttempt;
    string? _lastDeviceError;
    TimeSpan _statsThemeStart = TimeSpan.MinValue;
    bool _capWarned;
    double _renderMs;
    double _pushMs;

    Frame? _handoff;
    string? _lastRenderError; // the render thread only
    string? _lastSendError; // the sending thread only
    long _framesPushed;
    Thread? _thread;
    CancellationTokenSource? _stop;

    public RenderLoop(IDisplayDevice device, SensorStore sensors, ILog log, int? fpsOverride = null, LConnectDetector? lconnect = null)
    {
        _device = device;
        _sensors = sensors;
        _log = log;
        FpsOverride = fpsOverride;
        _lconnect = lconnect ?? LConnectDetector.Default;
    }

    /// Raised on the sending thread after each frame is pushed (or skipped while the screen is away). The image is
    /// disposed when handlers return, so copy it.
    public event Action<SKImage>? FrameRendered;

    /// `--fps` (or config.yaml's fps): the rate for every theme, or null for each theme's canvas.fps. Thread-safe; it
    /// takes effect from the next frame, so a host switching theme and rate together sets it as it hands over the theme.
    public int? FpsOverride
    {
        get => (int?)_fpsOverride;
        set => _fpsOverride = value;
    }

    /// The target rate: FpsOverride if set, else the theme's canvas.fps, clamped to 1–60.
    public int Fps => Math.Clamp(FpsOverride ?? _theme?.Spec.Canvas.Fps ?? CanvasSpec.DefaultFps, 1, CanvasSpec.MaxFps);

    public RenderStats Stats { get; private set; } = new(0, 0, 0);

    public long FramesPushed => Interlocked.Read(ref _framesPushed);

    /// Thread-safe. The theme is swapped in at the start of the next frame and is then owned (and disposed) by the loop.
    /// A theme offered but replaced before any frame picked it up is disposed straight away.
    public void SetTheme(LoadedTheme theme) => Interlocked.Exchange(ref _pending, theme)?.Dispose();

    /// Renders and sends one frame on the calling thread. Tests and `--frames` call this directly with fixed times;
    /// don't mix it with Start().
    public void Step(TimeSpan time)
    {
        if (_thread is not null) throw new InvalidOperationException("Step can't be used while the loop is running.");
        if (Render(time) is not { } frame) return;
        try
        {
            Send(frame, time);
        }
        finally
        {
            frame.Image.Dispose();
        }
    }

    public void Start()
    {
        if (_thread is not null) throw new InvalidOperationException("The render loop is already running.");
        _stop?.Dispose(); // the last run's, cancelled by Stop
        _stop = new CancellationTokenSource();
        _clock.Start();
        _thread = new Thread(Run) { IsBackground = true, Name = "Marquee render loop" };
        _thread.Start();
    }

    /// Ends the render and sending threads; the sending thread closes the screen as it ends. Start may follow, and
    /// opens it again with the same theme.
    public void Stop()
    {
        _stop?.Cancel();
        _thread?.Join();
        _thread = null;
        _clock.Stop();

        // A frame the sender never took must not go to the screen as the first frame of a restart.
        Interlocked.Exchange(ref _handoff, null)?.Image.Dispose();
        _frameReady.Reset();
    }

    public void Dispose()
    {
        Stop();
        CloseDevice();
        _renderer?.Dispose();
        _theme?.Dispose();
        Interlocked.Exchange(ref _pending, null)?.Dispose();
        Interlocked.Exchange(ref _handoff, null)?.Image.Dispose();
        _fitSurface?.Dispose();
        _stop?.Dispose();
        _frameReady.Dispose();
    }

    /// The render thread: renders at the target rate and hands each frame to the sending thread.
    void Run()
    {
        using var resolution = TimerResolution.Begin();
        var token = _stop!.Token;
        var clock = _clock;
        var sender = new Thread(() => SendLoop(clock, token)) { IsBackground = true, Name = "Marquee screen sender" };
        sender.Start();
        var next = clock.Elapsed;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (Render(clock.Elapsed) is { } frame)
                {
                    Interlocked.Exchange(ref _handoff, frame)?.Image.Dispose(); // the sender never took the last one
                    _frameReady.Set();
                }
            }
            catch (Exception ex)
            {
                ReportRender($"Rendering a frame failed: {ex.Message}");
            }

            next += TimeSpan.FromSeconds(1.0 / Fps);
            var now = clock.Elapsed;
            if (next < now) next = now; // Running behind: carry on from here rather than bursting to catch up.
            else token.WaitHandle.WaitOne(next - now);
        }

        sender.Join();
    }

    /// The sending thread: pushes each handed-over frame, and closes the device when the loop stops.
    void SendLoop(Stopwatch clock, CancellationToken token)
    {
        WaitHandle[] wake = [_frameReady, token.WaitHandle];
        while (!token.IsCancellationRequested)
        {
            WaitHandle.WaitAny(wake);
            if (Interlocked.Exchange(ref _handoff, null) is not { } frame) continue;
            try
            {
                Send(frame, clock.Elapsed);
            }
            catch (Exception ex)
            {
                // Push handles device errors itself, so this is a FrameRendered handler, or Close failing inside Push.
                var message = $"Sending a frame failed: {ex.Message}";
                if (message != _lastSendError)
                {
                    _lastSendError = message;
                    _log.Error(message);
                }
            }
            finally
            {
                frame.Image.Dispose();
            }
        }

        try
        {
            CloseDevice();
        }
        catch (Exception ex)
        {
            // Not ReportDevice: it ends with "Retrying every N s.", which is wrong while shutting down.
            _log.Error($"Closing the screen failed: {ex.Message}");
        }
    }

    /// Draws the current theme fitted to the screen, or null before any theme has arrived. The caller owns the image.
    Frame? Render(TimeSpan time)
    {
        TakePendingTheme(time);
        if (_renderer is null) return null;

        var clock = Stopwatch.StartNew();
        var rendered = _renderer.Render(_sensors.Snapshot(), time);
        SKImage image;
        try
        {
            image = FitToDevice(rendered);
        }
        catch
        {
            rendered.Dispose();
            throw;
        }

        if (!ReferenceEquals(image, rendered)) rendered.Dispose();
        return new Frame(image, clock.Elapsed.TotalMilliseconds, Fps, _themeStart);
    }

    void Send(Frame frame, TimeSpan time)
    {
        var clock = Stopwatch.StartNew();
        Push(frame.Image, time);
        var pushMs = clock.Elapsed.TotalMilliseconds;
        FrameRendered?.Invoke(frame.Image);
        Record(time, frame, pushMs);
    }

    void TakePendingTheme(TimeSpan time)
    {
        var theme = Interlocked.Exchange(ref _pending, null);
        if (theme is null) return;

        // Build the new renderer first: if it throws, the old renderer is still intact.
        ThemeRenderer renderer;
        try
        {
            renderer = new ThemeRenderer(theme);
        }
        catch
        {
            theme.Dispose(); // the loop owns it now, and nothing else will
            throw;
        }

        _renderer?.Dispose();
        _theme?.Dispose();
        _theme = theme;
        _renderer = renderer;
        _themeStart = time;

        var design = theme.Spec.Canvas.Size;
        var screen = new SKSizeI(_device.Info.Width, _device.Info.Height);
        if (design != screen)
            _log.Warn($"Theme '{theme.Name}' is designed for {Shape(design)} ({design.Width}×{design.Height}), but the screen is set up " +
                $"for {Shape(screen)} ({screen.Width}×{screen.Height}). It is shown scaled down to fit; use a {Shape(screen)} theme " +
                "or change --orientation.");
    }

    static string Shape(SKSizeI size) => size.Height > size.Width ? "portrait" : "landscape";

    /// Letterboxes a frame whose size doesn't match the screen, rather than stretching it.
    SKImage FitToDevice(SKImage frame)
    {
        var (width, height) = (_device.Info.Width, _device.Info.Height);
        if (frame.Width == width && frame.Height == height) return frame;

        _fitSurface ??= SKSurface.Create(new SKImageInfo(width, height));
        var canvas = _fitSurface.Canvas;
        canvas.Clear(SKColors.Black);
        var scale = Math.Min((float)width / frame.Width, (float)height / frame.Height);
        var (w, h) = (frame.Width * scale, frame.Height * scale);
        canvas.DrawImage(frame, SKRect.Create((width - w) / 2, (height - h) / 2, w, h), new SKSamplingOptions(SKFilterMode.Linear));
        return _fitSurface.Snapshot();
    }

    void Push(SKImage frame, TimeSpan time)
    {
        if (!_deviceOpen)
        {
            if (time < _nextOpenAttempt) return;
            try
            {
                _device.Open();
                _deviceOpen = true;
            }
            catch (Exception ex)
            {
                _nextOpenAttempt = time + ReconnectDelay;
                ReportDevice(ex);
                return;
            }
        }

        try
        {
            _device.PushFrame(frame);
            Interlocked.Increment(ref _framesPushed);
            // Announce recovery only once a frame actually got through, so a flapping screen doesn't spam the log.
            if (_lastDeviceError is not null)
            {
                _lastDeviceError = null;
                _log.Info($"Connected to {_device.Info.Name}.");
            }
        }
        catch (Exception ex)
        {
            CloseDevice();
            _nextOpenAttempt = time + ReconnectDelay;
            ReportDevice(ex);
        }
    }

    void ReportDevice(Exception ex)
    {
        var hint = ex is DeviceBusyException ? LConnectDetector.LogHint(_lconnect.Detect()) : "";
        if (hint.Length > 0) hint = " " + hint;
        var message = $"The screen is unavailable: {ex.Message}{hint} Retrying every {ReconnectDelay.TotalSeconds:0} s.";
        if (message == _lastDeviceError) return;
        _lastDeviceError = message;
        _log.Error(message);
    }

    /// Logs each distinct render error once, not once per frame. Kept apart from device errors so a failing frame is
    /// never mistaken for a screen that needs a "Connected" announcement. Render thread only.
    void ReportRender(string message)
    {
        if (message == _lastRenderError) return;
        _lastRenderError = message;
        _log.Error(message);
    }

    void CloseDevice()
    {
        if (!_deviceOpen) return;
        _deviceOpen = false;
        _device.Close();
    }

    void Record(TimeSpan time, Frame frame, double pushMs)
    {
        if (frame.ThemeStart != _statsThemeStart)
        {
            // A new theme gets its own chance to keep up before being warned about.
            _statsThemeStart = frame.ThemeStart;
            _capWarned = false;
        }

        _frameTimes.Enqueue(time);
        while (time - _frameTimes.Peek() > StatsWindow) _frameTimes.Dequeue();
        var span = (time - _frameTimes.Peek()).TotalSeconds;
        var fps = _frameTimes.Count > 1 && span > 0 ? (_frameTimes.Count - 1) / span : 0;

        _renderMs = _renderMs == 0 ? frame.RenderMs : _renderMs * 0.9 + frame.RenderMs * 0.1;
        _pushMs = _pushMs == 0 ? pushMs : _pushMs * 0.9 + pushMs * 0.1;
        Stats = new RenderStats(fps, _renderMs, _pushMs);

        if (!_capWarned && _frameTimes.Count > 1 && time - frame.ThemeStart >= CapCheckDelay && fps < frame.TargetFps * 0.9)
        {
            _capWarned = true;
            _log.Warn(string.Create(CultureInfo.InvariantCulture,
                $"Running at {fps:0} fps instead of {frame.TargetFps}: a frame takes {_renderMs:0.0} ms to render and {_pushMs:0.0} ms to send to the screen."));
        }
    }
}
