using Marquee.Core.Devices;
using Marquee.Core.Rendering;
using Marquee.Host;
using SkiaSharp;

namespace Marquee.App.Tests.TestSupport;

/// The engine as the menu sees it, with every property settable and every call recorded with the thread it came on.
/// Elevated, as the logon task starts the tray, unless a test says otherwise.
sealed class FakeTrayEngine : ITrayEngine
{
    readonly object _gate = new();
    readonly List<(string Call, int Thread)> _calls = [];

    public EngineSettings? Settings { get; set; }

    public EngineStatus Status { get; set; } = EngineStatus.Stopped;

    public bool IsRunning { get; set; }

    public bool IsPaused { get; set; }

    public bool UsesDefaultTheme { get; set; }

    public string? ThemeError { get; set; }

    public IReadOnlyList<string> Problems { get; set; } = [];

    public bool IsElevated { get; set; } = true;

    public bool CheckForUpdates { get; set; } = true;

    /// True unless a test says otherwise, so only the tests of the one-time offer to take over see it.
    public bool HandoverChosen { get; set; } = true;

    public bool LConnectInstalled { get; set; } = true;

    /// Every call, as "SwitchTheme C:\...", "Pause" and so on, with the managed thread it ran on.
    public IReadOnlyList<(string Call, int Thread)> Calls
    {
        get
        {
            lock (_gate) return [.. _calls];
        }
    }

    public IEnumerable<string> CallNames => Calls.Select(call => call.Call);

    /// Shows `theme` on `screen` at 30 fps.
    public void Showing(string themeFolder, string screen = "Universal Screen 8.8")
    {
        Settings = new EngineSettings { ThemeFolder = themeFolder };
        Status = new EngineStatus(new RenderStats(29.6, 4, 20), 30, 100, Path.GetFileName(themeFolder), screen);
        IsRunning = true;
    }

    public void Start() => Record("Start");

    public bool SwitchTheme(string folder)
    {
        Record($"SwitchTheme {folder}");
        return true;
    }

    public bool SetOrientation(Orientation orientation)
    {
        Record($"SetOrientation {orientation}");
        return true;
    }

    public void Pause() => Record("Pause");

    public void Resume() => Record("Resume");

    public bool TakeOverScreen()
    {
        Record("TakeOverScreen");
        return true;
    }

    public void Dispose() => Record("Dispose");

    /// Puts something that isn't an engine call into Calls, so a test can check what came between them.
    public void Note(string what) => Record(what);

    public void RaiseThemeFailed(string error) => ThemeFailed?.Invoke(error);

    public void RaiseConfigFailed(string error) => ConfigFailed?.Invoke(error);

    public void RaiseScreenBusy(ScreenBusyInfo info) => ScreenBusy?.Invoke(info);

    public void RaiseHandoverFinished(HandoverResult result) => HandoverFinished?.Invoke(result);

    public void RaiseScreenOpened() => ScreenOpened?.Invoke();

    public void RaiseConfigApplied() => ConfigApplied?.Invoke();

    public void RaiseThemeLoaded(LoadedThemeInfo theme) => ThemeLoaded?.Invoke(theme);

    public void RaiseFrameRendered(SKImage frame) => FrameRendered?.Invoke(frame);

    public event Action<LoadedThemeInfo>? ThemeLoaded;

    public event Action<string>? ThemeFailed;

    public event Action<string>? ConfigFailed;

    public event Action? ConfigApplied;

    public event Action<SKImage>? FrameRendered;

    public event Action<ScreenBusyInfo>? ScreenBusy;

    public event Action<HandoverResult>? HandoverFinished;

    public event Action? ScreenOpened;

    void Record(string call)
    {
        lock (_gate) _calls.Add((call, Environment.CurrentManagedThreadId));
    }
}
