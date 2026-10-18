using System.Runtime.ExceptionServices;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Claude;

/// Claude Code's limits from `claude -p /usage`, for people whose sessions run in the desktop app, which doesn't run
/// status-line commands. It writes the same keys as the status-line bridge; whichever wrote last wins. A failed poll
/// is not retried until `Interval` has passed, so a missing or logged-out `claude` is not started every few seconds
/// under ProviderRunner's backoff. A poll that lists no limits writes nothing, fails with NoLimitsMessage, and waits
/// longer each time it happens in a row (NoLimitsWait).
public sealed class ClaudeUsageProvider : ISensorProvider
{
    /// How often `claude -p /usage` runs. Each run takes about 3 seconds and costs nothing.
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    /// The longest wait between polls while `/usage` keeps listing no limits.
    public static readonly TimeSpan MaxNoLimitsWait = TimeSpan.FromMinutes(30);

    internal const string NoLimitsMessage =
        "claude -p /usage listed no limits (an API-key login, or Claude Code couldn't fetch them and showed its last-known values); keeping the last values";

    internal const string NotFoundMessage =
        "claude.exe wasn't found on PATH, in ~/.local/bin or in the desktop app; Claude Code limits come only from the status line";

    readonly Func<string> _fetch;
    readonly Func<DateTimeOffset> _now;
    readonly Func<TimeSpan> _elapsed;
    Exception? _failure;
    TimeSpan _nextAttempt;
    int _pollsWithoutLimits;

    /// `fetch` returns the stream-json text `claude -p /usage` prints, or throws. `now` is the wall clock for
    /// `claude.updated_at`; `elapsed` is a monotonic clock for the waits between attempts (Environment.TickCount64 by default).
    public ClaudeUsageProvider(Func<string> fetch, Func<DateTimeOffset>? now = null, Func<TimeSpan>? elapsed = null)
    {
        _fetch = fetch;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _elapsed = elapsed ?? (() => TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    /// Runs the real `claude.exe`, wherever ClaudeCli.Find finds it, never elevated, and logs which one it runs.
    public ClaudeUsageProvider(ILog log)
        : this(RealFetch(log, ClaudeCli.Find, ProcessElevation.IsCurrentProcessElevated, AgentPaths.MarqueeData, ClaudeCli.RunProcess, UnelevatedProcess.Run))
    {
    }

    public string Name => "Claude Code limits (/usage)";
    public TimeSpan Interval => DefaultInterval;

    public void Poll(SensorStore store)
    {
        var started = _elapsed();
        if (_failure is { } failure && started < _nextAttempt)
            ExceptionDispatchInfo.Throw(failure); // the same failure, without asking claude again; the runner logs it once

        ClaudeStatus status;
        try
        {
            status = ClaudeUsageReport.Parse(_fetch());
        }
        catch (Exception ex)
        {
            _failure = ex;
            _nextAttempt = started + Interval;
            throw;
        }

        // When Claude Code can't fetch fresh limits, `/usage` still succeeds but lists none (its text shows last-known
        // bars); an API-key login has none at all. Writing nothing, not even claude.updated_at, keeps "updated just now"
        // off a blank reading, and earlier values stay until they expire at their reset. Failing makes it show in the log.
        if (status.FiveHour is null && status.SevenDay is null)
        {
            _pollsWithoutLimits++;
            _nextAttempt = started + NoLimitsWait(_pollsWithoutLimits);
            _failure = new InvalidOperationException(NoLimitsMessage);
            throw _failure;
        }

        ClaudeStatusLine.Apply(status, store, _now());
        _pollsWithoutLimits = 0;
        _failure = null;
    }

    public void Dispose() { }

    /// The wait after `polls` polls in a row that listed no limits: 5, 10, 20, then 30 minutes.
    internal static TimeSpan NoLimitsWait(int polls) =>
        TimeSpan.FromTicks(Math.Min(MaxNoLimitsWait.Ticks, DefaultInterval.Ticks << Math.Clamp(polls - 1, 0, 10)));

    /// The fetch the ILog constructor uses: finds `claude.exe` on every poll (so installing Claude later works), logs the
    /// path the first time and whenever it changes, and runs `/usage` through `runUnelevated` while Marquee is elevated.
    internal static Func<string> RealFetch(ILog log, Func<string?> find, Func<bool> isElevated, string workingDirectory,
        ProcessRunner run, ProcessRunner runUnelevated)
    {
        string? announced = null;
        return () =>
        {
            var exe = find() ?? throw new InvalidOperationException(NotFoundMessage);
            if (exe != announced)
            {
                log.Info($"Claude Code limits: running {exe} for /usage.");
                announced = exe;
            }

            return ClaudeCli.RunUsage(exe, ClaudeCli.UsageArguments, workingDirectory, ClaudeCli.UsageTimeout, isElevated() ? runUnelevated : run);
        };
    }
}
