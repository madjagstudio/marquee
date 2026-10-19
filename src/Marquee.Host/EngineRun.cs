using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Host.Resources;

namespace Marquee.Host;

/// One start of the engine: the screen and its render loop, the theme watcher, the providers and the servers.
/// MarqueeEngine makes a new one at each start; a settings change goes through Apply, which changes only the parts the
/// new settings touch. The sensor store and the Claude limits belong to the engine and outlive it. Not thread-safe: the
/// engine calls it under its lock.
sealed class EngineRun : IDisposable
{
    static readonly RenderStats Idle = new(0, 0, 0);

    readonly MarqueeEngine _engine;
    readonly ILog _log;
    readonly Dictionary<AgentSource, ISensorProvider> _agents = [];
    volatile IDisplayDevice? _device;
    volatile RenderLoop? _loop;
    ThemeWatcher? _watcher;
    ProviderRunner? _providers;
    ISensorProvider? _hardware;
    ISensorProvider? _fps;
    IDisposable? _statusLine;
    IDisposable? _http;
    volatile string? _themeName;
    volatile bool _paused; // Status reads it without the engine's lock
    bool _showing; // whether a theme has loaded; theme loads never overlap (one watcher at a time, or Apply under the lock)
    int _stepped;

    // The LED ring: its sender (live runs only), the ring of the theme on screen (null for none), and the lock that
    // keeps a hot reload's ring and a settings change's from crossing (see ShowRing).
    volatile RingSender? _ring;
    volatile RingSpec? _themeRing;
    readonly object _ringGate = new();

    EngineRun(MarqueeEngine engine, EngineSettings settings, bool live, bool paused, ILog log)
    {
        _engine = engine;
        Settings = settings;
        Live = live;
        Paused = paused;
        _log = log;
    }

    /// The settings this run has applied.
    public EngineSettings Settings { get; private set; }

    /// False for `--frames`: no render thread and no theme watching; RenderFrames drives the loop instead.
    public bool Live { get; }

    /// Whether rendering is paused and the screen closed (Pause). Only a live run renders on its own, so only a live
    /// run's screen is closed by it.
    public bool Paused
    {
        get => _paused;
        private set => _paused = value;
    }

    /// Only for a run that started. Read from any thread. While paused, nothing is rendered or sent, so the measured
    /// rate and costs are zero rather than the last ones measured before the pause.
    public EngineStatus Status =>
        new(Live && Paused ? Idle : _loop!.Stats, _loop!.Fps, _loop.FramesPushed, _themeName, _device!.Info.Name);

    /// Starts everything, or returns null (with the reason logged, and everything it had made released) when there is
    /// no screen or the theme doesn't load. A paused run starts with the screen closed.
    public static EngineRun? Start(MarqueeEngine engine, EngineSettings settings, bool live, bool paused, ILog log,
        IEngineServices services)
    {
        var run = new EngineRun(engine, settings, live, paused, log);
        try
        {
            if (run.TryStart(services)) return run;
        }
        catch
        {
            run.Dispose();
            throw;
        }

        run.Dispose();
        return null;
    }

    /// Renders `count` frames on the calling thread at fixed times (1/fps apart) and says how many reached the screen.
    public int RenderFrames(int count)
    {
        var before = _loop!.FramesPushed;
        for (var i = 0; i < count; i++, _stepped++)
            _loop.Step(TimeSpan.FromSeconds(_stepped / (double)_loop.Fps));
        return (int)(_loop.FramesPushed - before);
    }

    /// Moves the run to `next`, touching only what differs: a new theme or frame rate goes to the running loop on the
    /// open screen, an orientation or sink change replaces the screen, and a provider, port or key-limit change restarts
    /// only that provider or the HTTP server. What can fail (a theme that must load, a screen that must be there) is
    /// made before anything is let go of, so a false return means nothing changed.
    public bool Apply(EngineSettings next, IEngineServices services)
    {
        var old = Settings;
        if (next.Orientation != old.Orientation || next.Sink != old.Sink)
        {
            if (!ReplaceScreen(next, services)) return false;
        }
        else if (next.ThemeFolder != old.ThemeFolder || next.FpsOverride != old.FpsOverride)
        {
            if (!ReplaceTheme(next)) return false;
        }

        Settings = next;
        ApplyProviders(old, next, services);
        if (next.HttpPort != old.HttpPort || next.MaxCustomKeys != old.MaxCustomKeys)
        {
            _http?.Dispose();
            _http = null;
            StartHttp(next, services);
        }

        // The ring goes with the sink (frames written to files light no real ring); `ring:` changes only its colors.
        if (next.Sink != old.Sink) ReplaceRing(services);
        else if (next.Ring != old.Ring || next.RingColor != old.RingColor) ShowRing();

        return true;
    }

    /// Stops rendering; the sending thread closes the screen as it ends, so another program can have it. The
    /// providers, the servers and the theme watcher carry on, so values stay fresh and a saved theme still reloads.
    public void Pause()
    {
        if (Paused) return;
        Paused = true;
        if (Live) _loop!.Stop();
        _ring?.Pause(); // dark, and let go
    }

    /// Starts rendering again; the screen opens with the next frame, with the theme that was showing or has loaded since.
    public void Resume()
    {
        if (!Paused) return;
        Paused = false;
        if (Live) _loop!.Start();
        _ring?.Start();
    }

    /// Stops in the reverse order of TryStart: nothing reloads into a stopping loop, the loop's threads end and close
    /// the screen, then the servers and providers stop.
    public void Dispose()
    {
        _watcher?.Dispose();
        _ring?.Dispose(); // the ring is left dark
        _loop?.Dispose();
        _device?.Dispose();
        _http?.Dispose();
        _statusLine?.Dispose();
        _providers?.Dispose();
    }

    bool TryStart(IEngineServices services)
    {
        // The screen and the theme come first: they are what can fail, and when they do nothing has started yet.
        _device = services.CreateDevice(Settings);
        if (_device is null)
        {
            _log.Error(Strings.NoScreenFound);
            return false;
        }

        var store = _engine.Sensors;
        _loop = NewLoop(_device, Settings);
        _watcher = NewWatcher(Settings);
        // With no last good layout yet, a broken theme at startup has nothing to fall back to.
        if (!_watcher.Reload(retryIfLocked: false)) return false;

        _providers = new ProviderRunner(store, _log);
        _hardware = services.CreateHardwareProvider(Settings.Sensors, _log);
        if (_hardware is not null) _providers.Start(_hardware);
        var agents = AgentSources.Enabled(Settings.ClaudeSessions, Settings.ClaudeUsage, Settings.Codex);
        foreach (var agent in AgentSources.Create(agents, _log, Remembered(services)))
            _providers.Start(agent);
        if (Settings.GameFps) StartFps(services);
        _statusLine = services.StartStatusLineServer(store, _log);
        StartHttp(Settings, services);

        if (Live)
        {
            StartRing(services);
            _watcher.Start();
            if (!Paused) _loop.Start();
        }

        return true;
    }

    RenderLoop NewLoop(IDisplayDevice device, EngineSettings settings)
    {
        var loop = new RenderLoop(device, _engine.Sensors, _log, settings.FpsOverride, _engine.LConnect);
        loop.FrameRendered += _engine.OnFrameRendered;
        // The L-Connect handover. Only a live run hands over: `--frames` renders its frames and exits.
        loop.ScreenOpenChanged += _engine.OnScreenOpenChanged;
        if (Live) loop.ScreenBusy += _engine.OnScreenBusy;
        return loop;
    }

    /// Has the render loop try to open the screen with its next frame (RenderLoop.RetryNow), and the ring sender the
    /// ring with its next tick, for the L-Connect handover: L-Connect's service has just let go of both. Any thread.
    public void RetryScreen()
    {
        _loop?.RetryNow();
        _ring?.RetryNow();
    }

    ThemeWatcher NewWatcher(EngineSettings settings) =>
        new(settings.ThemeFolder, Loaded, Failed, fpsOverride: settings.FpsOverride);

    /// The theme folder or the frame rate changed: the new theme loads while the old one is still on screen, then goes
    /// to the running loop. The screen is never closed, and a theme that doesn't load changes nothing.
    bool ReplaceTheme(EngineSettings next)
    {
        if (LoadTheme(next) is not { } theme) return false;

        // The old watcher goes first, so a reload of the old theme already under way can't land after the new one.
        _watcher!.Dispose();
        _watcher = NewWatcher(next);
        _loop!.FpsOverride = next.FpsOverride;
        Loaded(theme);
        if (Live) _watcher.Start();
        return true;
    }

    /// The orientation or the sink changed, which takes a new device and a loop for it. The new device and the theme
    /// (loaded again: the old loop disposes its own) come first; the old screen closes only once both are ready.
    bool ReplaceScreen(EngineSettings next, IEngineServices services)
    {
        if (services.CreateDevice(next) is not { } device)
        {
            _log.Error(Strings.NoScreenFound);
            return false;
        }

        if (LoadTheme(next) is not { } theme)
        {
            device.Dispose();
            return false;
        }

        _watcher!.Dispose();
        _loop!.Dispose(); // its sending thread closes the old screen as it ends
        _device!.Dispose();
        _device = device;
        _loop = NewLoop(device, next);
        _watcher = NewWatcher(next);
        Loaded(theme);
        if (Live)
        {
            _watcher.Start();
            if (!Paused) _loop.Start();
        }

        return true;
    }

    /// Null, with the error reported as a failed reload would be, when the theme doesn't load.
    LoadedTheme? LoadTheme(EngineSettings settings)
    {
        try
        {
            return ThemeWatcher.Load(settings.ThemeFolder, settings.FpsOverride);
        }
        catch (ThemeException ex)
        {
            Failed(ex);
            return null;
        }
    }

    /// Stops the providers whose switch went off and starts those whose switch came on; the rest keep running, with
    /// their state (an open sensor driver, a PresentMon session) intact.
    void ApplyProviders(EngineSettings old, EngineSettings next, IEngineServices services)
    {
        if (next.Sensors != old.Sensors)
        {
            if (_hardware is not null) _providers!.Stop(_hardware);
            _hardware = services.CreateHardwareProvider(next.Sensors, _log);
            if (_hardware is not null) _providers!.Start(_hardware);
        }

        var before = AgentSources.Enabled(old.ClaudeSessions, old.ClaudeUsage, old.Codex);
        var after = AgentSources.Enabled(next.ClaudeSessions, next.ClaudeUsage, next.Codex);
        foreach (var source in before.Except(after))
            if (_agents.Remove(source, out var provider)) _providers!.Stop(provider);
        foreach (var agent in AgentSources.Create(after.Except(before), _log, Remembered(services)))
            _providers!.Start(agent);

        if (next.GameFps != old.GameFps)
        {
            if (_fps is not null) _providers!.Stop(_fps);
            _fps = null;
            if (next.GameFps) StartFps(services);
        }
    }

    /// PresentMon reads ETW, which takes an elevated Marquee; unelevated, the warning says why FPS is off.
    void StartFps(IEngineServices services)
    {
        if (services.IsElevated) _providers!.Start(_fps = services.CreateFpsProvider(_log));
        else _log.Warn(Strings.FpsNeedsAdministrator);
    }

    /// Makes agent providers through the services and remembers which source each came from, so Apply can stop one.
    Func<AgentSource, ILog, ISensorProvider> Remembered(IEngineServices services) =>
        (source, log) => _agents[source] = services.CreateAgentProvider(source, log);

    void StartHttp(EngineSettings settings, IEngineServices services)
    {
        if (settings.HttpPort is { } port)
            _http = services.StartCustomValuesServer(_engine.Sensors, _log, port, settings.MaxCustomKeys);
    }

    void Loaded(LoadedTheme theme)
    {
        foreach (var warning in theme.Warnings) _log.Warn(warning);
        _log.Info(Strings.ShowingTheme(theme.Name));
        var info = new LoadedThemeInfo(theme.Name, theme.Warnings);
        _themeName = theme.Name;
        _showing = true;
        _themeRing = theme.Spec.Ring;
        ShowRing();
        _loop!.SetTheme(theme); // the loop owns (and disposes) it from here
        _engine.OnThemeLoaded(info);
    }

    void Failed(ThemeException error)
    {
        _log.Error(_showing ? Strings.LastGoodLayoutStays(error.Message) : error.Message);
        _engine.OnThemeFailed(error.Message);
    }

    /// Live runs only (`--frames` leaves the ring alone). The ring gets its colors before its thread starts, so it
    /// never shows anything else first; a paused run makes the sender but starts it only at Resume.
    void StartRing(IEngineServices services)
    {
        _ring = services.CreateRing(Settings, _engine.Sensors, _log);
        ShowRing();
        if (!Paused) _ring?.Start();
    }

    /// The sink changed, and a sink folder gets no ring. The old sender darkens the ring as it goes.
    void ReplaceRing(IEngineServices services)
    {
        if (!Live) return;
        _ring?.Dispose();
        StartRing(services);
    }

    /// Gives the ring what config.yaml's `ring:` and the theme on screen say. A settings change calls this on the
    /// engine's thread and a hot reload on a theme watcher's; under one lock, each reads the latest settings and theme
    /// ring, so whichever call comes last leaves the ring right.
    void ShowRing()
    {
        lock (_ringGate)
        {
            if (_ring is { } ring) ring.Spec = RingChoice.Resolve(Settings.Ring, Settings.RingColor, _themeRing);
        }
    }
}
