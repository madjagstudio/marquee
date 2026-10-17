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

/// Renders the current theme at its frame rate and sends each frame to the device. IDisplayDevice is single-threaded,
/// so the loop owns it: it opens it, pushes every frame from its own thread, reconnects after failures, and closes it
/// when stopped. The caller still disposes the device. PushFrame is synchronous (about 21 ms on the Universal Screen),
/// so the device, not the renderer, usually sets the ceiling (about 47 fps).
public sealed class RenderLoop : IDisposable
{
    public static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);
    static readonly TimeSpan CapCheckDelay = TimeSpan.FromSeconds(3);
    static readonly TimeSpan StatsWindow = TimeSpan.FromSeconds(1);

    readonly IDisplayDevice _device;
    readonly SensorStore _sensors;
    readonly ILog _log;
    readonly int? _fpsOverride;
    readonly LConnectDetector _lconnect;
    readonly Queue<TimeSpan> _frameTimes = new();

    LoadedTheme? _pending;
    LoadedTheme? _theme;
    ThemeRenderer? _renderer;
    SKSurface? _fitSurface;
    bool _deviceOpen;
    TimeSpan _nextOpenAttempt;
    string? _lastDeviceError;
    string? _lastRenderError;
    TimeSpan _themeStart;
    bool _capWarned;
    double _renderMs;
    double _pushMs;
    long _framesPushed;
    Thread? _thread;
    CancellationTokenSource? _stop;

    public RenderLoop(IDisplayDevice device, SensorStore sensors, ILog log, int? fpsOverride = null, LConnectDetector? lconnect = null)
    {
        _device = device;
        _sensors = sensors;
        _log = log;
        _fpsOverride = fpsOverride;
        _lconnect = lconnect ?? LConnectDetector.Default;
    }

    /// Raised on the render thread after each frame is pushed. The image is disposed when handlers return, so copy it.
    public event Action<SKImage>? FrameRendered;

    /// The target rate: --fps if given, else the theme's canvas.fps, clamped to 1–60.
    public int Fps => Math.Clamp(_fpsOverride ?? _theme?.Spec.Canvas.Fps ?? CanvasSpec.DefaultFps, 1, CanvasSpec.MaxFps);

    public RenderStats Stats { get; private set; } = new(0, 0, 0);

    public long FramesPushed => Interlocked.Read(ref _framesPushed);

    /// Thread-safe. The theme is swapped in at the start of the next frame and is then owned (and disposed) by the loop.
    /// A theme offered but replaced before any frame picked it up is disposed straight away.
    public void SetTheme(LoadedTheme theme) => Interlocked.Exchange(ref _pending, theme)?.Dispose();

    /// Renders and pushes one frame. The thread started by Start() calls this; tests call it directly with fixed times.
    public void Step(TimeSpan time)
    {
        TakePendingTheme(time);
        if (_renderer is null) return;

        var clock = Stopwatch.StartNew();
        using var rendered = _renderer.Render(_sensors.Snapshot(), time);
        var frame = FitToDevice(rendered);
        var renderMs = clock.Elapsed.TotalMilliseconds;

        try
        {
            clock.Restart();
            Push(frame, time);
            var pushMs = clock.Elapsed.TotalMilliseconds;
            FrameRendered?.Invoke(frame);
            Record(time, renderMs, pushMs);
        }
        finally
        {
            if (!ReferenceEquals(frame, rendered)) frame.Dispose();
        }
    }

    public void Start()
    {
        if (_thread is not null) throw new InvalidOperationException("The render loop is already running.");
        _stop = new CancellationTokenSource();
        _thread = new Thread(Run) { IsBackground = true, Name = "Marquee render loop" };
        _thread.Start();
    }

    public void Stop()
    {
        _stop?.Cancel();
        _thread?.Join();
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        CloseDevice();
        _renderer?.Dispose();
        _theme?.Dispose();
        Interlocked.Exchange(ref _pending, null)?.Dispose();
        _fitSurface?.Dispose();
        _stop?.Dispose();
    }

    void Run()
    {
        using var resolution = TimerResolution.Begin();
        var token = _stop!.Token;
        var clock = Stopwatch.StartNew();
        var next = TimeSpan.Zero;

        while (!token.IsCancellationRequested)
        {
            try
            {
                Step(clock.Elapsed);
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
        _capWarned = false;

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
    /// never mistaken for a screen that needs a "Connected" announcement.
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

    void Record(TimeSpan time, double renderMs, double pushMs)
    {
        _frameTimes.Enqueue(time);
        while (time - _frameTimes.Peek() > StatsWindow) _frameTimes.Dequeue();
        var span = (time - _frameTimes.Peek()).TotalSeconds;
        var fps = _frameTimes.Count > 1 && span > 0 ? (_frameTimes.Count - 1) / span : 0;

        _renderMs = _renderMs == 0 ? renderMs : _renderMs * 0.9 + renderMs * 0.1;
        _pushMs = _pushMs == 0 ? pushMs : _pushMs * 0.9 + pushMs * 0.1;
        Stats = new RenderStats(fps, _renderMs, _pushMs);

        if (!_capWarned && _frameTimes.Count > 1 && time - _themeStart >= CapCheckDelay && fps < Fps * 0.9)
        {
            _capWarned = true;
            _log.Warn(string.Create(CultureInfo.InvariantCulture,
                $"Running at {fps:0} fps instead of {Fps}: a frame takes {_renderMs:0.0} ms to render and {_pushMs:0.0} ms to send to the screen."));
        }
    }
}
