using Marquee.Core.Config;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Host;

namespace Marquee.Editor.Model.Values;

/// What the editor reads itself, for when the tray's feed has nothing: behind an interface, so tests never touch real
/// sensors.
public interface ILocalReadings : IDisposable
{
    bool IsRunning { get; }

    /// Starts reading, if it isn't already.
    void Start();

    /// Stops reading and lets go of the hardware, keeping what was read; Start reads again.
    void Stop();

    /// What has been read, stale values included.
    SensorSnapshot Snapshot();
}

/// The editor's own readings, through Marquee.Host's providers, unelevated, as far as config.yaml lets the tray read
/// (LocalSources): LibreHardwareMonitor's readable parts (GPU, memory and loads; it already drops the zeros it reports
/// for sensors it can't read) or HWiNFO's shared memory, and the Claude Code and Codex providers, which read files in
/// the user's profile. config.yaml is read when the editor starts: a change to it is picked up by the next editor.
///
/// They run under a ProviderRunner, as the tray's do, rather than a lighter poll of the editor's own: each provider on
/// its own thread and schedule, with the tray's backoff and a stop that never hangs on a poll stuck in a driver call.
/// So the keys, units and ttls are the tray's, and the poll of the feed and the UI thread never wait on a sensor.
///
/// Claude Code limits from `claude -p /usage` are left out: that provider starts `claude.exe` for about 3 seconds every
/// 5 minutes, which the tray already does; the editor shows a sample for them when the tray isn't running.
public sealed class LocalReadings : ILocalReadings
{
    readonly IReadOnlyList<Func<ISensorProvider>> _providers;
    readonly ILog _log;
    readonly SensorStore _store;
    readonly object _gate = new();
    ProviderRunner? _runner;
    Task _stopping = Task.CompletedTask;
    bool _disposed;

    /// Reads with the providers `providers` make, each made afresh on every Start. One that can't be made is logged
    /// and left out, and the rest run.
    public LocalReadings(IEnumerable<Func<ISensorProvider>> providers, TimeProvider? time = null, ILog? log = null)
    {
        _providers = providers.ToList();
        _log = log ?? NullLog.Instance;
        _store = new SensorStore((time ?? TimeProvider.System).GetUtcNow);
    }

    /// The real providers, those config.yaml's `sensors:` and `providers:` allow (LocalSources): never start them in
    /// tests. The hardware source is made by the tray's factory (SensorSources).
    public static LocalReadings ForThisPc(MarqueeConfig config, ILog? log = null)
    {
        log ??= NullLog.Instance;
        var sources = LocalSources.Of(config);
        var providers = new List<Func<ISensorProvider>>();
        if (sources.Hardware is { } hardware)
            providers.Add(() => SensorSources.Create(hardware, log) ?? throw new InvalidOperationException($"No sensor source '{hardware}'."));
        foreach (var source in sources.Agents)
            providers.Add(() => AgentSources.Build(source, log));
        return new LocalReadings(providers, log: log) { Sources = sources };
    }

    /// What ForThisPc read with; null for readings made from a list of providers.
    public LocalSources? Sources { get; private init; }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _runner is not null;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _runner is not null) return;
            _runner = new ProviderRunner(_store, _log);
            foreach (var make in _providers)
            {
                try
                {
                    _runner.Start(make());
                }
                catch (Exception ex)
                {
                    _log.Error($"A sensor source couldn't start: {ex.Message}");
                }
            }
        }
    }

    /// Returns at once: the runner waits for each provider's current poll before disposing it, up to 3 s for one stuck
    /// in a driver call, and that wait is the pool's, not the window's.
    public void Stop()
    {
        lock (_gate)
        {
            if (_runner is not { } runner) return;
            _runner = null;
            _stopping = Task.WhenAll(_stopping, Task.Run(runner.Dispose));
        }
    }

    public SensorSnapshot Snapshot() => _store.Snapshot();

    /// Stops the providers and waits for them, those Stop left stopping included, so nothing reads the hardware once it
    /// returns.
    public void Dispose()
    {
        ProviderRunner? runner;
        Task stopping;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            runner = _runner;
            _runner = null;
            stopping = _stopping;
        }

        runner?.Dispose();
        stopping.Wait();
    }
}
