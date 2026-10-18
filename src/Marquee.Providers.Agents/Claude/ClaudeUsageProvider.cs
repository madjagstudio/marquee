using System.Runtime.ExceptionServices;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Claude;

/// Claude Code's limits from `claude -p /usage`, for people whose sessions run in the desktop app, which doesn't run
/// status-line commands. It writes the same keys as the status-line bridge; whichever wrote last wins. A failed poll
/// is not retried until `Interval` has passed, so a missing or logged-out `claude` is not started every few seconds
/// under ProviderRunner's backoff.
public sealed class ClaudeUsageProvider : ISensorProvider
{
    /// How often `claude -p /usage` runs. Each run takes about 3 seconds and costs nothing.
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    readonly Func<string> _fetch;
    readonly Func<DateTimeOffset> _now;
    Exception? _failure;
    DateTimeOffset _lastAttempt;

    /// `fetch` returns the stream-json text `claude -p /usage` prints, or throws.
    public ClaudeUsageProvider(Func<string> fetch, Func<DateTimeOffset>? now = null)
    {
        _fetch = fetch;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// Runs the real `claude.exe`, wherever ClaudeCli.Find finds it.
    public ClaudeUsageProvider() : this(RunClaude) { }

    public string Name => "Claude Code limits (/usage)";
    public TimeSpan Interval => DefaultInterval;

    public void Poll(SensorStore store)
    {
        var started = _now();
        if (_failure is { } failure && started - _lastAttempt < Interval)
            ExceptionDispatchInfo.Throw(failure); // the same failure, without asking claude again; the runner logs it once

        _lastAttempt = started;
        try
        {
            var status = ClaudeUsageReport.Parse(_fetch());
            ClaudeStatusLine.Apply(status, store, _now());
            _failure = null;
        }
        catch (Exception ex)
        {
            _failure = ex;
            throw;
        }
    }

    public void Dispose() { }

    static string RunClaude()
    {
        var exe = ClaudeCli.Find()
            ?? throw new InvalidOperationException(
                "claude.exe wasn't found on PATH, in ~/.local/bin or in the desktop app; Claude Code limits come only from the status line");
        return ClaudeCli.RunUsage(exe, AgentPaths.MarqueeData, ClaudeCli.UsageTimeout);
    }
}
