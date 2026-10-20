using System.Windows.Threading;

namespace Marquee.Editor.Canvas;

/// When the canvas draws a frame (spec Part D):
/// - after each change, and each time the values move on (about once a second): Request, which draws once, soon,
///   however many requests come first, and no more often than MaxFramesPerSecond, so a drag's stream of mouse moves
///   draws at the screen's pace rather than once a move;
/// - at the theme's fps while something moves on its own (a video, a shader, a pulse, smoothing settling): Animates
///   says when, asked again after every frame;
/// - never while inactive (the window minimised).
///
/// Lives on the UI thread, and draws there.
public sealed class RedrawScheduler : IDisposable
{
    public const int MaxFramesPerSecond = 60;

    static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1.0 / MaxFramesPerSecond);

    readonly Action _draw;
    readonly Func<bool> _animates;
    readonly TimeProvider _time;
    readonly Dispatcher _dispatcher;
    readonly DispatcherTimer _animation;
    readonly DispatcherTimer _later;
    long _lastDraw;
    bool _queued;
    bool _active = true;
    bool _disposed;

    /// Made on the UI thread, whose dispatcher it draws on.
    public RedrawScheduler(Action draw, Func<bool> animates, TimeProvider time)
    {
        _draw = draw;
        _animates = animates;
        _time = time;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _animation = new DispatcherTimer(DispatcherPriority.Render, _dispatcher);
        _animation.Tick += (_, _) => Draw();
        _later = new DispatcherTimer(DispatcherPriority.Render, _dispatcher);
        _later.Tick += (_, _) =>
        {
            _later.Stop();
            Draw();
        };
        Fps = 30;
    }

    /// The theme's frame rate, for drawing while it animates.
    public int Fps
    {
        set => _animation.Interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(value, 1, MaxFramesPerSecond));
    }

    /// The time between frames while the theme animates: one over Fps, no shorter than MaxFramesPerSecond allows.
    public TimeSpan FrameInterval => _animation.Interval;

    public bool IsActive => _active;

    /// Whether frames are being drawn at the theme's fps now.
    public bool IsAnimating => _animation.IsEnabled;

    /// Draws a frame soon.
    public void Request()
    {
        if (!_active || _disposed || _queued || _later.IsEnabled) return;
        var wait = MinInterval - _time.GetElapsedTime(_lastDraw);
        if (wait > TimeSpan.Zero)
        {
            _later.Interval = wait;
            _later.Start();
            return;
        }

        _queued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _queued = false;
            Draw();
        });
    }

    /// Starts or stops drawing at the theme's fps as Animates now says.
    public void UpdateAnimation()
    {
        if (_active && !_disposed && _animates())
        {
            if (!_animation.IsEnabled) _animation.Start();
        }
        else
        {
            _animation.Stop();
        }
    }

    /// Stops all drawing while the window is minimised, and draws a frame at once when it comes back.
    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (active)
        {
            Request();
            return;
        }

        _animation.Stop();
        _later.Stop();
    }

    public void Dispose()
    {
        _disposed = true;
        _animation.Stop();
        _later.Stop();
    }

    void Draw()
    {
        if (!_active || _disposed) return;
        _lastDraw = _time.GetTimestamp();
        _draw();
        UpdateAnimation();
    }
}
