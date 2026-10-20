using Marquee.App.Resources;
using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Core.Files;
using Marquee.Core.Logging;
using Marquee.Host;
using SkiaSharp;

namespace Marquee.App;

/// The engine as the tray runs it, the way `marquee run` does but with no command line: settings from config.yaml, with
/// FirstRun.DefaultTheme when it names no theme; config.yaml followed while running; and, since at logon the screen
/// may not be there yet, the screen tried again whenever a device arrives (RetrySoon) and on a timer until it is.
///
/// While the tray wants the screen (an engine exists and isn't paused) it holds the screen marker (TrayScreen), so
/// `marquee run` says the tray has the screen instead of that it is busy.
///
/// The engine is made once settings first resolve: when config.yaml names a theme that can't be found there is no
/// engine, only the problem, until an edit names one that can. Every method is thread-safe and may block (engine
/// calls load themes and stop threads), so the tray calls them on its EngineWorker; the config watcher and the retry
/// timer call in from their own threads.
sealed class TrayRuntime : ITrayEngine
{
    /// How often the screen is tried again while there is none, besides whenever a device arrives. A failed try logs a
    /// line, so this is slow; plugging the screen in is noticed at once anyway.
    public static readonly TimeSpan DefaultRetryEvery = TimeSpan.FromMinutes(1);

    /// After a device arrives: Windows announces a USB device in a burst of changes, and the screen's driver needs a
    /// moment after the first of them.
    static readonly TimeSpan RetryAfterDeviceChange = TimeSpan.FromSeconds(1);

    readonly TrayPaths _paths;
    readonly ILog _log;
    readonly IEngineServices _services;
    readonly string _screenMarker;
    readonly TimeSpan _retryEvery;
    readonly object _gate = new();
    volatile MarqueeEngine? _engine;
    volatile string? _configError; // config.yaml's own error, until it reads cleanly
    volatile string? _settingsError; // the theme config.yaml names can't be found, until one can
    volatile string? _themeError; // the last theme error, until a theme loads
    volatile bool _checkForUpdates = true; // config.yaml's `updates:`, as last read cleanly
    volatile bool _handoverChosen; // config.yaml sets `lconnect.handover`, as last read
    volatile bool _lconnectInstalled = true; // L-Connect's service is installed, or the tray couldn't tell
    ConfigWatcher? _watcher;
    volatile Timer? _retry; // RetrySoon reads it from the UI thread
    IDisposable? _marker;
    bool _started;
    bool _disposed;
    volatile bool _namesNoTheme; // config.yaml as last read names no theme; the tray reads it on an engine thread

    /// `screenMarker` is TrayNames.Screen, or a test's own name.
    public TrayRuntime(TrayPaths paths, ILog log, IEngineServices services, string screenMarker, TimeSpan? retryEvery = null)
    {
        _paths = paths;
        _log = log;
        _services = services;
        _screenMarker = screenMarker;
        _retryEvery = retryEvery ?? DefaultRetryEvery;
    }

    public event Action<LoadedThemeInfo>? ThemeLoaded;

    public event Action<string>? ThemeFailed;

    public event Action<string>? ConfigFailed;

    public event Action<SKImage>? FrameRendered;

    public event Action? ConfigApplied;

    public event Action<ScreenBusyInfo>? ScreenBusy;

    public event Action<HandoverResult>? HandoverFinished;

    public event Action? ScreenOpened;

    public EngineSettings? Settings => _engine?.Settings;

    public EngineStatus Status => _engine?.Status ?? EngineStatus.Stopped;

    public bool IsRunning => _engine?.IsRunning ?? false;

    public bool IsPaused => _engine?.IsPaused ?? false;

    /// config.yaml as last read names no theme, and has no error. One that doesn't read shows the example theme too, but
    /// that isn't a first run: the first-run hint would cover its error's balloon and use up the one-time hint. The tray
    /// asks when the screen opens, which can be long after Start, so this follows each edit.
    public bool UsesDefaultTheme => _namesNoTheme && _configError is null;

    public string? ThemeError => _themeError;

    public bool IsElevated => _services.IsElevated;

    public bool CheckForUpdates => _checkForUpdates;

    public bool HandoverChosen => _handoverChosen;

    public bool LConnectInstalled => _lconnectInstalled;

    public IReadOnlyList<string> Problems
    {
        get
        {
            var fps = _engine?.Settings is { GameFps: true } && !_services.IsElevated ? Strings.FpsNeedsAdministrator : null;
            string?[] problems = [_configError, _settingsError, _themeError is { } theme ? Strings.ThemeProblem(theme) : null, fps];
            // Then each provider that is failing (LibreHardwareMonitor or PresentMon missing, `claude` not found), which
            // otherwise only the log would say.
            return [.. problems.OfType<string>(), .. _engine?.ProviderProblems ?? []];
        }
    }

    /// Reads config.yaml, starts the engine, then follows config.yaml and starts trying the screen again until it is
    /// there. False from the engine (no screen yet, a theme that doesn't load) isn't an error here: the engine stays
    /// wanted, and the retries and the next config.yaml edit start it.
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;

            var config = ReadConfig();
            _checkForUpdates = config.CheckForUpdates;
            Follow(config);

            // %LOCALAPPDATA%\Marquee may not exist yet on a fresh install; the watcher needs the folder to see a
            // config.yaml written into it later, as `marquee run` does. The tray runs elevated, so a missing one is made
            // only through a path without a junction or link on it.
            try
            {
                var folder = Path.GetDirectoryName(_paths.ConfigFile)!;
                if (!Directory.Exists(folder)) SafeFile.CreateDirectory(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Error(Strings.ConfigNotWatched(ex.Message));
            }

            _watcher = new ConfigWatcher(_paths.ConfigFile, OnConfigChanged, OnConfigFailed);
            _watcher.Start();
            _retry = new Timer(_ => Retry(), null, _retryEvery, _retryEvery);
            _lconnectInstalled = IsLConnectInstalled();
        }
    }

    /// A device arrived: try the screen again shortly, if there isn't one yet.
    public void RetrySoon()
    {
        try
        {
            _retry?.Change(RetryAfterDeviceChange, _retryEvery);
        }
        catch (ObjectDisposedException)
        {
            // The tray is quitting.
        }
    }

    public bool SwitchTheme(string folder)
    {
        lock (_gate) return !_disposed && _engine is { } engine && engine.SwitchTheme(folder);
    }

    public bool SetOrientation(Orientation orientation)
    {
        lock (_gate)
            return !_disposed && _engine is { } engine && engine.Apply(engine.Settings with { Orientation = orientation });
    }

    /// Closes the screen and lets go of the marker, so `marquee run` (or anything else) can have the screen.
    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _engine?.Pause();
            UpdateMarker();
        }
    }

    /// Takes the marker and the screen again; with no run yet (the screen wasn't there), starts one at once rather
    /// than at the next retry.
    public void Resume()
    {
        lock (_gate)
        {
            if (_disposed || _engine is not { } engine) return;
            engine.Resume();
            UpdateMarker();
            if (!engine.IsRunning) engine.Start();
        }
    }

    /// MarqueeEngine.TakeOverScreen, which takes no engine lock and returns at once.
    public bool TakeOverScreen()
    {
        lock (_gate) return !_disposed && _engine is { } engine && engine.TakeOverScreen();
    }

    /// Stops the watcher and the timer first, outside the lock (each waits for a callback in progress, which takes the
    /// lock), then the engine, which lets go of the screen and saves the Claude limits, then the marker.
    public void Dispose()
    {
        ConfigWatcher? watcher;
        Timer? retry;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            (watcher, _watcher) = (_watcher, null);
            (retry, _retry) = (_retry, null);
        }

        watcher?.Dispose();
        if (retry is not null)
        {
            using var stopped = new ManualResetEvent(false);
            if (retry.Dispose(stopped)) stopped.WaitOne();
        }

        lock (_gate)
        {
            _engine?.Dispose();
            UpdateMarker();
        }
    }

    /// Under _gate. Settles the settings (no command line, so config.yaml then the defaults) and makes the engine the
    /// first time they resolve, or applies them to it.
    void Follow(MarqueeConfig config)
    {
        _handoverChosen = config.HandoverSet;
        _namesNoTheme = config.Theme is null;
        var resolved = SettingsResolver.Resolve(new RunOptions(), FirstRun.WithDefaultTheme(config), _paths.Locator, _paths.ConfigFile);
        if (!resolved.Succeeded)
        {
            // A running engine keeps its settings, as for any bad edit; with none yet, nothing shows until this is fixed.
            _settingsError = resolved.Error;
            _log.Error(resolved.Error);
            return;
        }

        _settingsError = null;
        if (_engine is { } engine)
        {
            engine.Apply(resolved.Settings);
            return;
        }

        _engine = NewEngine(resolved.Settings);
        UpdateMarker();
        _engine.Start();
    }

    MarqueeEngine NewEngine(EngineSettings settings)
    {
        var engine = new MarqueeEngine(settings, _log, _services);
        engine.ThemeLoaded += theme =>
        {
            _themeError = null;
            ThemeLoaded?.Invoke(theme);
        };
        engine.ThemeFailed += error =>
        {
            _themeError = error;
            ThemeFailed?.Invoke(error);
        };
        engine.FrameRendered += frame => FrameRendered?.Invoke(frame);
        engine.ScreenBusy += info =>
        {
            if (info.LConnect == LConnectState.Service) _lconnectInstalled = true;
            ScreenBusy?.Invoke(info);
        };
        engine.HandoverFinished += result => HandoverFinished?.Invoke(result);
        engine.ScreenOpened += () => ScreenOpened?.Invoke();
        return engine;
    }

    /// Under _gate. A config.yaml that is missing is the defaults, silently; a broken or unreadable one is the defaults
    /// too, with the reason logged, in the menu and in a balloon, so the tray still starts.
    MarqueeConfig ReadConfig()
    {
        string problem;
        try
        {
            return ConfigFile.Read(_paths.ConfigFile);
        }
        catch (ConfigException ex)
        {
            problem = Strings.ConfigInvalid(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = Strings.ConfigUnreadable(ex.Message);
        }

        _configError = problem;
        _log.Error(problem);
        ConfigFailed?.Invoke(problem);
        return MarqueeConfig.Default;
    }

    /// Raises ConfigApplied after letting go of the lock, so a handler may call back in.
    void OnConfigChanged(MarqueeConfig config)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _configError = null;
            _checkForUpdates = config.CheckForUpdates;
            Follow(config);
        }

        ConfigApplied?.Invoke();
    }

    /// A broken edit keeps the settings that are running, as `marquee run` does.
    void OnConfigFailed(ConfigException error)
    {
        var problem = Strings.ConfigChangeInvalid(error.Message);
        lock (_gate)
        {
            if (_disposed) return;
            _configError = problem;
        }

        _log.Error(problem);
        ConfigFailed?.Invoke(problem);
    }

    /// Under _gate, once at Start. Reading a service's state needs no administrator rights. A Service Control Manager
    /// that can't be asked counts as installed, so the menu still offers what might work.
    bool IsLConnectInstalled()
    {
        try
        {
            return _services.CreateServiceControl().Status(LConnectHandover.ServiceName) is not null;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// On the timer's thread, where an exception would end the process.
    void Retry()
    {
        try
        {
            lock (_gate)
            {
                if (!_disposed && _engine is { IsRunning: false } engine) engine.Start();
            }
        }
        catch (Exception ex)
        {
            _log.Error(Strings.ScreenRetryFailed(ex.Message));
        }
    }

    /// Under _gate. The marker is held exactly while an engine exists, isn't paused and the tray hasn't quit. Making
    /// it can only fail when a marker from another process with other access is still open (an elevated tray that is
    /// just quitting, seen from an unelevated one); `marquee run` then still sees that one.
    void UpdateMarker()
    {
        var wanted = !_disposed && _engine is { IsPaused: false };
        if (!wanted)
        {
            _marker?.Dispose();
            _marker = null;
            return;
        }

        try
        {
            _marker ??= TrayScreen.Hold(_screenMarker);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            _log.Warn(Strings.ScreenMarkerFailed(ex.Message));
        }
    }
}
