using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Host.Resources;
using Marquee.Providers.Agents.Claude;
using SkiaSharp;

namespace Marquee.Host;

/// Everything Marquee shows on the screen, wired together once for `marquee run` and the tray: the sensor store, the
/// providers, the status-line pipe and HTTP servers, the screen, the render loop and the theme watcher (themes still
/// reload when saved), and the saved Claude Code limits. It has no UI; hosts listen to its events.
///
/// Start, Apply, SwitchTheme, Pause, Resume, Stop and Dispose may be called from any thread (Apply usually comes from
/// the config watcher's); they take one lock, and Start and Apply load themes on the calling thread, so a UI host calls
/// them from a worker thread. Status takes no lock, so a status line or an event handler never waits for a change to
/// finish.
public sealed class MarqueeEngine : IDisposable
{
    /// How often the Claude Code limits are saved while running, if they changed, as `marquee run` always has.
    public static readonly TimeSpan SaveLimitsEvery = TimeSpan.FromSeconds(1);

    /// Settings that couldn't be applied because their theme didn't load, and the watch on that theme's folder.
    sealed class Waiting(EngineSettings settings)
    {
        public EngineSettings Settings { get; } = settings;
        public ThemeWatcher? Watcher { get; set; }
    }

    readonly ILog _log;
    readonly IEngineServices _services;
    readonly ClaudeLimitsFile? _claudeLimits; // none for a sink folder
    readonly EngineEvents _events;
    readonly EngineHandover _handover;
    readonly object _gate = new();
    readonly object _saveGate = new();
    volatile EngineRun? _run;
    volatile EngineSettings _settings;
    volatile bool _paused;
    Timer? _saveTimer;
    Waiting? _waiting; // settings that couldn't be applied because their theme didn't load, and the watch on that theme
    bool _wanted; // Start was called and Stop wasn't, so a missing run (a start or a change that failed) starts again
    bool _live = true;
    bool _restored;
    bool _disposed;

    public MarqueeEngine(EngineSettings settings, ILog log, IEngineServices services)
    {
        _settings = settings;
        _log = log;
        _services = services;
        _events = new EngineEvents(log);
        // A sink run renders to files, often beside the tray or in a test: restoring would put what another run saw
        // into its frames, and saving would race the tray for the file. So an engine made for a sink keeps no limits.
        _claudeLimits = settings.Sink is null ? services.CreateClaudeLimits(log) : null;
        _handover = new EngineHandover(log, services, mode: () => _settings.Handover, retryScreen: () => _run?.RetryScreen(),
            busy: info => Post(() => ScreenBusy?.Invoke(info)),
            finished: result => Post(() => HandoverFinished?.Invoke(result)));
    }

    /// The settings the engine runs with, or will run with at the next Start.
    public EngineSettings Settings => _settings;

    /// Every sensor value. It lives as long as the engine, so values and their history survive Apply.
    public SensorStore Sensors { get; } = new();

    public bool IsRunning => _run is not null;

    /// Whether Pause holds: the screen is closed until Resume.
    public bool IsPaused => _paused;

    public EngineStatus Status => _run?.Status ?? EngineStatus.Stopped;

    /// The providers that are failing right now, a line each ("FPS failed: PresentMon is missing…"), until each works
    /// again; none while stopped. For a status line or the tray's menu, which have no log to show. Takes no lock, as
    /// Status doesn't.
    public IReadOnlyList<string> ProviderProblems => _run?.ProviderProblems ?? [];

    /// The theme folder that settings Apply couldn't apply are waiting for (see Apply), for tests.
    internal string? WaitingForTheme
    {
        get
        {
            lock (_gate) return _waiting?.Settings.ThemeFolder;
        }
    }

    /// A theme loaded: at start, after its files changed, or for Apply and SwitchTheme. The theme events are raised
    /// with no engine or watcher lock held, one at a time and in the order they happened. For a load that Start, Apply
    /// or SwitchTheme did, that is usually on the calling thread, just before the call returns; but if another thread
    /// is raising events at that moment (a hot reload's slow handler, say), that thread raises this one too, after the
    /// call has returned. For a hot reload, or a change that waited for its theme (see Apply), it is a thread-pool
    /// thread. So a handler may block, wait for a UI thread (Control.Invoke) or call back into the engine, and must not
    /// count on running on the thread that made the change. A handler that throws is logged.
    public event Action<LoadedThemeInfo>? ThemeLoaded;

    /// A theme failed to load: the error, with its file and line. At start this means Start returns false; for Apply,
    /// that the previous theme stays; for a hot reload, that the last good layout stays on screen; for a theme that
    /// settings are waiting for (see Apply), that it still doesn't load. Raised like ThemeLoaded.
    public event Action<string>? ThemeFailed;

    /// Each frame after it went to the screen, raised on the render loop's screen-sending thread. The image is disposed
    /// when the handlers return, so copy it. Stop, Pause and a screen change wait for that thread to end, so a handler
    /// must return promptly: never wait for a thread that may be in an engine call (on a UI thread use BeginInvoke, not
    /// Invoke), and never call the engine itself.
    public event Action<SKImage>? FrameRendered;

    /// The screen came up busy: who holds it and what `lconnect.handover` says (see ScreenBusyInfo). Raised once per
    /// busy episode, which ends when Marquee gets the screen or pauses, so Ask's tray balloon shows once, not at every
    /// retry. With Off it changes nothing, but the tray can still offer "Take over" while CanTakeOver is true. Raised
    /// like ThemeLoaded, with no lock held.
    public event Action<ScreenBusyInfo>? ScreenBusy;

    /// A handover (Auto's, or TakeOverScreen's) ended, with how. Raised like ThemeLoaded, with no lock held.
    public event Action<HandoverResult>? HandoverFinished;

    /// The engine opened the screen (the first frame after opening it got through), which ends a busy episode however
    /// it ended. Raised like ThemeLoaded, through the same queue as ScreenBusy, so it always comes after the ScreenBusy
    /// of the episode it ends: a tray that clears its busy state here never has a late ScreenBusy set it again.
    public event Action? ScreenOpened;

    /// The tray's "Take over" (or a click on Ask's balloon): takes the screen from L-Connect on a thread of its own,
    /// whatever `lconnect.handover` says, and returns at once; HandoverFinished says how it went. False when it doesn't
    /// start: a handover is under way already, the engine isn't running live or is paused, Marquee has the screen
    /// already, or Marquee isn't elevated (those two are logged). Any thread; takes no engine lock.
    public bool TakeOverScreen() => _handover.TakeOver();

    /// Restores the saved Claude Code limits (the first time only, before any provider, so live readings win; never for
    /// a sink folder), then starts everything for Settings. False, with the reason logged, when there is no screen or
    /// the theme doesn't load: with no last good layout yet, there is nothing to show. The engine still counts as started, so the next
    /// Apply (or another Start) tries again. `liveRendering` false leaves the render thread and theme watching off, for
    /// RenderFrames. After Pause, it starts with the screen closed.
    public bool Start(bool liveRendering = true)
    {
        bool started;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null) return true;

            if (!_restored)
            {
                _claudeLimits?.Restore(Sensors, DateTimeOffset.UtcNow);
                _restored = true;
            }

            _wanted = true;
            _live = liveRendering;
            started = StartRun(_settings);
        }

        _events.Raise();
        return started;
    }

    /// `--frames N`: renders `count` frames at fixed times and says how many reached the screen. Needs
    /// Start(liveRendering: false) first.
    public int RenderFrames(int count)
    {
        lock (_gate)
        {
            if (_run is not { Live: false } run)
                throw new InvalidOperationException("RenderFrames needs Start(liveRendering: false) first.");
            return run.RenderFrames(count);
        }
    }

    /// Switches to new settings (a config.yaml edit), changing only what differs (see EngineRun.Apply): a new theme or
    /// frame rate keeps the screen open, and the sensor store and the Claude limits always carry on. Settings equal to
    /// the current ones change nothing. If the new settings can't be applied (a broken theme, no screen for a new
    /// orientation), nothing changes and this returns false; when it was another theme that didn't load, the engine
    /// watches that theme's folder and applies the settings once a save makes it load, unless other settings are
    /// applied or the engine stops first. Before Start or after Stop, it only replaces the settings Start will use. If
    /// the engine should be running but isn't, because a start or an earlier change failed, it starts with the new
    /// settings.
    public bool Apply(EngineSettings settings)
    {
        bool applied;
        lock (_gate)
        {
            if (_disposed) return false;
            applied = ApplyLocked(settings);
        }

        _events.Raise();
        return applied;
    }

    /// Shows another theme folder (a full path) with every other setting kept, as Apply does for a `theme:` edit.
    public bool SwitchTheme(string folder)
    {
        bool applied;
        lock (_gate)
        {
            if (_disposed) return false;
            applied = ApplyLocked(_settings with { ThemeFolder = Path.GetFullPath(folder) });
        }

        _events.Raise();
        return applied;
    }

    /// Stops rendering and closes the screen so another program can have it, until Resume. The providers, the servers
    /// and the theme watcher run on, so values stay fresh, and settings changes still apply (a new screen stays closed
    /// too). It holds across Stop and Start. Repeating it does nothing.
    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _paused = true;
            _handover.SetScreenWanted(false);
            _run?.Pause();
        }
    }

    /// Opens the screen and renders again after Pause.
    public void Resume()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _paused = false;
            _handover.SetScreenWanted(_run is { Live: true });
            _run?.Resume();
        }
    }

    /// Stops everything in the reverse order it started, then saves the Claude limits one last time. Start may be
    /// called again afterwards. Repeating it does nothing.
    public void Stop()
    {
        lock (_gate)
        {
            _wanted = false;
            _handover.SetScreenWanted(false);
            StopWaiting();
            StopSaving();
            var run = _run;
            _run = null;
            run?.Dispose();
            // A handover under way has stopped waiting for the screen; it ends once L-Connect is running again.
            _handover.WaitForEnd();
            // After the providers have stopped, so the file gets their last word.
            if (_restored) SaveLimits();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            _handover.Dispose();
        }
    }

    internal void OnThemeLoaded(LoadedThemeInfo theme) => Post(() => ThemeLoaded?.Invoke(theme));

    internal void OnThemeFailed(string error) => Post(() => ThemeFailed?.Invoke(error));

    internal void OnFrameRendered(SKImage frame) => FrameRendered?.Invoke(frame);

    /// Who holds a busy screen, for the render loops.
    internal LConnectDetector LConnect => _services.LConnect;

    /// A live render loop's sending thread found the screen busy; takes no engine lock (Stop waits for that thread).
    internal void OnScreenBusy(LConnectState state) => _handover.OnScreenBusy(state);

    /// A render loop opened or closed the screen; takes no engine lock. The render loop reports a busy screen on the
    /// same thread before it opens it, so ScreenOpened is queued after that ScreenBusy.
    internal void OnScreenOpenChanged(bool open)
    {
        _handover.OnScreenOpenChanged(open);
        if (open) Post(() => ScreenOpened?.Invoke());
    }

    /// Under _gate.
    bool ApplyLocked(EngineSettings settings)
    {
        StopWaiting(); // these settings replace any that were waiting for their theme
        if (_run is not { } run)
        {
            _settings = settings;
            return !_wanted || StartRun(settings);
        }

        if (settings == run.Settings) return true;
        _log.Info(Strings.SettingsChanged);
        try
        {
            if (run.Apply(settings, _services))
            {
                _settings = settings;
                return true;
            }

            _log.Error(Strings.PreviousSettingsKept);
            WaitForTheme(settings, run.Settings);
            return false;
        }
        catch (Exception ex)
        {
            // Part-way through a change the run is in no known state, so it stops altogether; the engine still wants
            // to run, so the next Apply starts it afresh with whatever settings that brings.
            _log.Error(Strings.SettingsChangeFailed(ex.Message));
            _settings = settings;
            _run = null;
            _handover.SetScreenWanted(false);
            DisposeRun(run);
            return false;
        }
    }

    /// Under _gate, after Apply kept the running settings. A change to a theme that doesn't load yet (picked while it
    /// is still being written, say) is applied once it does: the theme's folder is watched as the one on screen is, a
    /// save that loads applies the settings again, and a save that still doesn't load is reported as a hot reload is.
    /// Only while rendering live, and only for a theme other than the one on screen, which its own watcher reloads.
    /// A failure that isn't the theme's (no screen for a new orientation) is tried again at the theme's next save too.
    void WaitForTheme(EngineSettings wanted, EngineSettings running)
    {
        if (!_live || wanted.ThemeFolder == running.ThemeFolder || !Directory.Exists(wanted.ThemeFolder)) return;

        var waiting = new Waiting(wanted);
        // The callbacks run under the watcher's lock, which disposing it (under _gate) waits for, so they never take
        // _gate: the change itself is applied from the thread pool.
        var watcher = new ThemeWatcher(wanted.ThemeFolder,
            loaded: theme =>
            {
                theme.Dispose(); // Apply loads it again, before it lets go of the theme on screen
                ThreadPool.QueueUserWorkItem(_ => ApplyWaiting(waiting));
            },
            failed: error =>
            {
                _log.Error(Strings.ThemeStillFails(error.Message));
                OnThemeFailed(error.Message);
            },
            fpsOverride: wanted.FpsOverride);
        try
        {
            watcher.Start();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // The folder went away just now; there is nothing to wait for.
            watcher.Dispose();
            return;
        }

        waiting.Watcher = watcher;
        _waiting = waiting;
        _log.Info(Strings.WaitingForTheme(Path.GetFileName(wanted.ThemeFolder)));
    }

    /// The theme a change was waiting for loaded: applies the change, unless another change, Stop or Dispose came first.
    void ApplyWaiting(Waiting waiting)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || _waiting != waiting) return;
                ApplyLocked(waiting.Settings);
            }

            _events.Raise();
        }
        catch (Exception ex)
        {
            // This runs on a thread-pool thread, where an unhandled exception ends the process.
            _log.Error(Strings.CouldNotApplyConfig(ex.Message));
        }
    }

    /// Under _gate.
    void StopWaiting()
    {
        _waiting?.Watcher?.Dispose();
        _waiting = null;
    }

    /// Under _gate. False, with the reason logged, when the run doesn't start; an unexpected error is logged too,
    /// rather than thrown at a config watcher's thread.
    bool StartRun(EngineSettings settings)
    {
        // Before the run starts: its render loop may find the screen busy before EngineRun.Start returns, and Auto must
        // take it then.
        _handover.SetScreenWanted(_live && !_paused);
        try
        {
            _run = EngineRun.Start(this, settings, _live, _paused, _log, _services);
        }
        catch (Exception ex)
        {
            _log.Error(Strings.StartFailed(ex.Message));
            _handover.SetScreenWanted(false);
            return false;
        }

        if (_run is null)
        {
            _handover.SetScreenWanted(false);
            return false;
        }

        if (_claudeLimits is not null) _saveTimer ??= new Timer(_ => SaveLimits(), null, SaveLimitsEvery, SaveLimitsEvery);
        return true;
    }

    /// Stops what is left of a run that failed part-way. The failure is logged already; the run failing to stop as well
    /// adds nothing a user could act on, and must not stop Apply from returning.
    static void DisposeRun(EngineRun run)
    {
        try
        {
            run.Dispose();
        }
        catch
        {
            // See above.
        }
    }

    /// Queues a theme event. An engine call holding the gate raises it once it has let go, before returning; anything
    /// else (a hot reload, on a theme watcher's thread and under its lock) leaves it to the thread pool.
    void Post(Action raise)
    {
        _events.Enqueue(raise);
        if (!Monitor.IsEntered(_gate)) ThreadPool.QueueUserWorkItem(_ => _events.Raise());
    }

    /// Waits for a save the timer is in the middle of, so the last save in Stop is the last word.
    void StopSaving()
    {
        if (_saveTimer is null) return;
        using var stopped = new ManualResetEvent(false);
        if (_saveTimer.Dispose(stopped)) stopped.WaitOne();
        _saveTimer = null;
    }

    /// Writes only when the limits changed. ClaudeLimitsFile handles file errors itself; anything else must not end
    /// the process from a timer thread.
    void SaveLimits()
    {
        try
        {
            lock (_saveGate) _claudeLimits?.Save(Sensors.Snapshot());
        }
        catch (Exception ex)
        {
            _log.Error(Strings.SavingLimitsFailed(ex.Message));
        }
    }
}
