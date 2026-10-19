using Marquee.Core.Devices;
using Marquee.Host;
using SkiaSharp;

namespace Marquee.App;

/// The engine as the tray menu sees it (TrayRuntime; a fake in tests). Every method may block for a while (themes
/// load, render threads stop), so the menu calls them on its EngineWorker, never on the UI thread. The properties
/// take no lock and are cheap, for building the menu. Events are raised on engine threads.
interface ITrayEngine : IDisposable
{
    /// Reads config.yaml and starts the engine. Called once.
    void Start();

    /// What the engine runs with, or null while there is no engine because no theme could be found.
    EngineSettings? Settings { get; }

    EngineStatus Status { get; }

    /// Whether the engine is showing (or paused over) a theme on a screen; false while it waits for a screen.
    bool IsRunning { get; }

    bool IsPaused { get; }

    /// config.yaml named no theme at start, so FirstRun.DefaultTheme is showing.
    bool UsesDefaultTheme { get; }

    /// The last theme error, until a theme loads.
    string? ThemeError { get; }

    /// What the user should know is wrong, one line each: config.yaml errors, a theme that can't be found or doesn't
    /// load, a provider that is off for a reason or failing right now (MarqueeEngine.ProviderProblems).
    IReadOnlyList<string> Problems { get; }

    /// Shows another theme folder; false when it didn't load (the engine shows it once a save makes it load).
    bool SwitchTheme(string folder);

    /// Turns the picture for another way of mounting the screen.
    bool SetOrientation(Orientation orientation);

    /// Closes the screen so another program can have it, until Resume.
    void Pause();

    void Resume();

    event Action<LoadedThemeInfo>? ThemeLoaded;

    event Action<string>? ThemeFailed;

    /// config.yaml was changed but can't be used; the previous settings stay. The message says why, with its line.
    event Action<string>? ConfigFailed;

    /// Each frame sent to the screen, for the preview. The image is disposed when the handlers return.
    event Action<SKImage>? FrameRendered;
}
