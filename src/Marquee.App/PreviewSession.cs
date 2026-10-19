using Marquee.App.Resources;
using Marquee.Core.Devices;
using Marquee.Host;
using Marquee.Preview;

namespace Marquee.App;

/// The preview window opened from the tray, wired to the engine as `marquee run --preview` wires it: every frame, the
/// theme's warnings or error in the banner, and the frame rate in the status bar once a second. The window runs on its
/// own thread (PreviewHost), so the engine never waits for it.
sealed class PreviewSession : IDisposable
{
    static readonly TimeSpan StatusEvery = TimeSpan.FromSeconds(1);

    readonly ITrayEngine _engine;
    readonly PreviewHost _window;
    readonly Timer _status;
    int _disposed;

    PreviewSession(ITrayEngine engine, EngineSettings settings)
    {
        _engine = engine;
        _window = PreviewHost.Open(Path.GetFileName(settings.ThemeFolder), settings.Orientation.CanvasSize());
        _window.ShowError(engine.ThemeError);
        _window.Closed += () =>
        {
            Dispose();
            Closed?.Invoke();
        };
        engine.FrameRendered += _window.ShowFrame;
        engine.ThemeLoaded += OnThemeLoaded;
        engine.ThemeFailed += _window.ShowError;
        _status = new Timer(_ => ShowStatus(), null, StatusEvery, StatusEvery);
    }

    /// Raised on the preview's thread when the user closes the window.
    public event Action? Closed;

    /// Null when there is nothing to preview yet (no theme found).
    public static PreviewSession? Open(ITrayEngine engine) =>
        engine.Settings is { } settings ? new PreviewSession(engine, settings) : null;

    /// Closes the window, if the user hasn't, and stops listening to the engine.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _engine.FrameRendered -= _window.ShowFrame;
        _engine.ThemeLoaded -= OnThemeLoaded;
        _engine.ThemeFailed -= _window.ShowError;
        _status.Dispose();
        _window.Dispose();
    }

    void OnThemeLoaded(LoadedThemeInfo theme) =>
        _window.ShowError(theme.Warnings.Count > 0 ? string.Join(Environment.NewLine, theme.Warnings) : null);

    void ShowStatus()
    {
        var status = _engine.Status;
        _window.ShowStatus(Strings.PreviewStatus(status.Stats.Fps, status.TargetFps, status.Stats.RenderMs, status.Stats.PushMs, status.FramesSent));
    }
}
