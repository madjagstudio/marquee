using Marquee.Core.Devices;
using Marquee.Core.Rendering;
using Marquee.Host;
using SkiaSharp;

namespace Marquee.App.Tests.TestSupport;

/// The engine as the menu sees it, with every property settable and every call recorded with the thread it came on.
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

    public void Dispose() => Record("Dispose");

    public void RaiseThemeFailed(string error) => ThemeFailed?.Invoke(error);

    public void RaiseConfigFailed(string error) => ConfigFailed?.Invoke(error);

    public event Action<LoadedThemeInfo>? ThemeLoaded;

    public event Action<string>? ThemeFailed;

    public event Action<string>? ConfigFailed;

    public event Action<SKImage>? FrameRendered;

    void Record(string call)
    {
        lock (_gate) _calls.Add((call, Environment.CurrentManagedThreadId));
    }

    // No test raises these two yet; without a use, the compiler warns that the events are never raised.
    public void RaiseThemeLoaded(LoadedThemeInfo theme) => ThemeLoaded?.Invoke(theme);

    public void RaiseFrameRendered(SKImage frame) => FrameRendered?.Invoke(frame);
}
