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

    /// Whether the tray runs as administrator, as its logon task starts it. Taking the screen from L-Connect and
    /// installing updates need it.
    bool IsElevated { get; }

    /// config.yaml's `updates:` (true unless `off`), as last read cleanly; true when there is no config.yaml. Unlike
    /// Settings, it is there even while no theme can be found.
    bool CheckForUpdates { get; }

    /// Whether config.yaml sets `lconnect.handover` at all, as last read (one broken at start counts as not setting
    /// it). Off then is the user's choice, not the default, so the tray doesn't offer the handover on its own.
    bool HandoverChosen { get; }

    /// Whether L-Connect's service is installed, as far as the tray could tell at start (true when it couldn't), or
    /// has been seen holding the screen since. Without it, "Always take over at start" has nothing to do.
    bool LConnectInstalled { get; }

    /// Shows another theme folder; false when it didn't load (the engine shows it once a save makes it load).
    bool SwitchTheme(string folder);

    /// Turns the picture for another way of mounting the screen.
    bool SetOrientation(Orientation orientation);

    /// Closes the screen so another program can have it, until Resume.
    void Pause();

    void Resume();

    /// Takes the screen from L-Connect on a thread of its own, whatever `lconnect.handover` says (the tray's "Take over"
    /// item and Ask's balloon); HandoverFinished says how it went. False when it doesn't start: one is under way, the
    /// engine is paused or not running, or the tray isn't elevated. Returns at once.
    bool TakeOverScreen();

    event Action<LoadedThemeInfo>? ThemeLoaded;

    event Action<string>? ThemeFailed;

    /// config.yaml was changed but can't be used; the previous settings stay. The message says why, with its line.
    event Action<string>? ConfigFailed;

    /// config.yaml was changed and read cleanly, and its settings now apply (CheckForUpdates among them).
    event Action? ConfigApplied;

    /// The screen came up busy, once per busy episode (MarqueeEngine.ScreenBusy): who holds it, whether to show Ask's
    /// prompt, and whether a take-over could work.
    event Action<ScreenBusyInfo>? ScreenBusy;

    /// A handover (Auto's, or TakeOverScreen's) ended, with how.
    event Action<HandoverResult>? HandoverFinished;

    /// The engine opened the screen, which ends a busy episode (MarqueeEngine.ScreenOpened): always after the
    /// ScreenBusy of that episode.
    event Action? ScreenOpened;

    /// Each frame sent to the screen, for the preview. The image is disposed when the handlers return.
    event Action<SKImage>? FrameRendered;
}
