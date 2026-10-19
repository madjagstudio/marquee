using System.Diagnostics;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Fps;

/// The frame rate of the game in front: `fps` and `frametime` (ms) over the last second, `fps.1pct_low` over the last
/// 10 seconds, and `fps.app`, the game's process name. PresentMon reports every presenting process's frames; each poll
/// picks the foreground window's process out of them, ignoring Marquee itself and dwm (which presents the desktop). The
/// keys expire 2 seconds after that process's last frame arrived, so they go stale (a theme shows "—", or hides them
/// with `when: stale`) once the game stops presenting or something without frames comes to the front.
///
/// PresentMon reads ETW, which needs administrator rights: the engine only makes this provider when Marquee is
/// elevated. It starts on the first poll, its exe's hash checked first, and runs until Dispose. If it exits, the poll
/// that notices throws (ProviderRunner logs it and backs off) and the next one starts a new run with new frame stats.
/// It records in an ETW session of this process's own (PresentMonSession), which Dispose stops.
public sealed class FpsProvider : ISensorProvider
{
    /// How long after the foreground process's last frame its keys go stale.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);

    readonly string _exe;
    readonly string _sha256;
    readonly Func<string, IReadOnlyList<string>, IPresentMonRun> _launch;
    readonly Func<int?> _foreground;
    readonly Func<int, string?> _processName;
    readonly Func<TimeSpan> _clock;
    readonly int _ownProcessId;
    readonly PresentMonSession _session;
    readonly string[] _arguments;
    bool _started;
    PresentMonProcess? _presentMon;
    (int Id, string? Name)? _lastProcess;

    /// The bundled PresentMon, the real foreground window, and Marquee's own process id.
    public FpsProvider()
        : this(PresentMonBundle.DefaultPath, PresentMonBundle.Sha256, PresentMonChild.Start, ForegroundProcess.Id,
            ForegroundProcess.Name, Monotonic(), Environment.ProcessId,
            EtwSessions.Instance, PresentMonSession.IsRunning)
    {
    }

    /// `launch` starts PresentMon (exe, arguments); `processName` gives a process's name without ".exe", or null if it
    /// has gone; `clock` is monotonic; `isRunning` says whether a process id is a running process.
    internal FpsProvider(string exe, string sha256, Func<string, IReadOnlyList<string>, IPresentMonRun> launch,
        Func<int?> foreground, Func<int, string?> processName, Func<TimeSpan> clock, int ownProcessId,
        IEtwSessions sessions, Func<int, bool> isRunning)
    {
        _exe = exe;
        _sha256 = sha256;
        _launch = launch;
        _foreground = foreground;
        _processName = processName;
        _clock = clock;
        _ownProcessId = ownProcessId;
        _session = new PresentMonSession(sessions, isRunning, ownProcessId);
        // --stop_existing_session ends a session of this name left by a run of this process that crashed, or by a
        // Marquee that had this process id before, which would otherwise hold the name.
        _arguments = ["--output_stdout", "--no_console_stats", "--session_name", _session.Name, "--stop_existing_session"];
    }

    public string Name => "FPS";

    public TimeSpan Interval => TimeSpan.FromMilliseconds(250);

    public void Poll(SensorStore store)
    {
        var presentMon = Running();

        if (_foreground() is not { } processId || processId == _ownProcessId) return;
        if (ProcessName(processId) is not { } name || name.Equals("dwm", StringComparison.OrdinalIgnoreCase)) return;
        if (!presentMon.Stats.TryRead(processId, out var reading)) return;

        // The keys expire StaleAfter after the last frame, not after this poll.
        var ttl = StaleAfter - (_clock() - reading.LastArrival);
        if (ttl <= TimeSpan.Zero) return;

        store.Set("fps", reading.Fps, ttl);
        store.Set("frametime", reading.FrameTimeMs, ttl);
        store.Set("fps.1pct_low", reading.OnePercentLowFps, ttl);
        store.Set("fps.app", name, ttl);
    }

    public void Dispose()
    {
        _presentMon?.Dispose();
        _presentMon = null;
        if (_started) _session.Stop();
    }

    /// The current run, started if there is none. A run that has ended is dropped and its reason thrown, so the next
    /// poll (after ProviderRunner's backoff) starts a new one.
    PresentMonProcess Running()
    {
        if (_presentMon is { Failure: { } failure } ended)
        {
            _presentMon = null;
            ended.Dispose();
            throw new InvalidOperationException(failure);
        }

        if (_presentMon is not null) return _presentMon;

        _session.StopOrphans();
        // Held open from the hash check until PresentMon has started, so the file can't be swapped in between.
        using (PresentMonBundle.OpenVerified(_exe, _sha256))
        {
            var run = _launch(_exe, _arguments);
            _started = true;
            return _presentMon = new PresentMonProcess(run, _clock);
        }
    }

    static Func<TimeSpan> Monotonic()
    {
        var watch = Stopwatch.StartNew();
        return () => watch.Elapsed;
    }

    /// The foreground process's name, looked up again only when another process comes to the front.
    string? ProcessName(int processId)
    {
        if (_lastProcess is not { } last || last.Id != processId)
            _lastProcess = last = (processId, _processName(processId));
        return last.Name;
    }
}
