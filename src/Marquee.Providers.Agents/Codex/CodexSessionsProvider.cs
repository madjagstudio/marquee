using System.Text.Json;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Codex;

/// Counts Codex sessions as the rollout logs with an event in the last 10 minutes: codex.sessions.total, .busy and .idle.
/// Running processes can't be used: the Codex desktop app runs every thread inside one app-server process. Logs of
/// sub-agents and reviews (session_meta with a parent_thread_id) belong to a session already counted, so they're skipped.
/// A session is busy when its last turn event is task_started, idle after task_complete or turn_aborted.
/// Activity comes from the newest event's own timestamp, because NTFS last-write times of files that stay open lag
/// (a log can hold events minutes newer than its write time). The write time is only a coarse 24 h prefilter of which
/// logs to read, and the fallback for a log whose tail has no timestamp.
public sealed class CodexSessionsProvider(string folder, Func<DateTimeOffset>? now = null) : ISensorProvider
{
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(10);

    /// Logs not written within this long are never read. Coarse on purpose: write times lag, so they can't be trusted
    /// for the 10 minute window.
    static readonly TimeSpan CandidateWindow = TimeSpan.FromHours(24);

    /// session_meta, the first line of each log, is about 20 KB.
    const int FirstLineBytes = 256 * 1024;

    /// How far back to look for the newest event and the last turn event. Without a turn event this far back, a turn
    /// has been working for a long time.
    const long TailBytes = 1024 * 1024;

    static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    /// Whether each log is a sub-agent's. A log's first line never changes once it is complete, so it is read once then.
    readonly Dictionary<string, bool> _subagent = new(StringComparer.OrdinalIgnoreCase);

    public CodexSessionsProvider() : this(AgentPaths.CodexSessions) { }

    public string Name => "Codex sessions";
    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public void Poll(SensorStore store)
    {
        var now = _now();
        var candidateSince = (now - CandidateWindow).UtcDateTime;
        var activeSince = now - ActiveWindow;
        var candidates = RolloutFile.NewestFirst(folder).TakeWhile(f => f.LastWriteTimeUtc >= candidateSince).ToList();

        int busy = 0, idle = 0;
        foreach (var file in candidates)
        {
            try
            {
                var tail = ReadTail(file.FullName, activeSince);
                var active = tail.Newest is { } newest ? newest >= activeSince : new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) >= activeSince;
                if (!active || IsSubagent(file.FullName)) continue;
                if (tail.Busy) busy++;
                else idle++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Deleted or locked since it was listed; the next poll sees it as it is then.
            }
        }

        var names = candidates.Select(f => f.FullName).ToList();
        foreach (var gone in _subagent.Keys.Except(names, StringComparer.OrdinalIgnoreCase).ToList())
            _subagent.Remove(gone);

        store.Set("codex.sessions.total", busy + idle, Ttl);
        store.Set("codex.sessions.busy", busy, Ttl);
        store.Set("codex.sessions.idle", idle, Ttl);
    }

    public void Dispose() { }

    bool IsSubagent(string path)
    {
        if (_subagent.TryGetValue(path, out var subagent)) return subagent;

        // No complete first line yet (Codex is still writing session_meta) or one too long to read: it counts as a
        // session of its own for this poll, and the next poll reads it again.
        if (RolloutFile.FirstLine(path, FirstLineBytes) is not { } firstLine) return false;

        return _subagent[path] = HasParent(firstLine);
    }

    /// True for a session_meta line naming a parent thread. A line that doesn't parse counts as a session of its own.
    static bool HasParent(string firstLine)
    {
        try
        {
            using var document = JsonDocument.Parse(firstLine);
            return JsonFields.String(document.RootElement, "type") == "session_meta"
                && JsonFields.Object(document.RootElement, "payload") is { } payload
                && JsonFields.String(payload, "parent_thread_id") is { Length: > 0 };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// The newest event's timestamp and whether the last turn event leaves the session busy, from one backwards read of
    /// the tail. Stops as soon as both are known, or as soon as the newest event is older than `activeSince` (the log
    /// is inactive then, and Busy doesn't matter). Newest is null when no line in the tail has a timestamp.
    static (DateTimeOffset? Newest, bool Busy) ReadTail(string path, DateTimeOffset activeSince)
    {
        DateTimeOffset? newest = null;
        bool? busy = null;
        foreach (var line in RolloutFile.LinesFromEnd(path, TailBytes))
        {
            if (newest is null && Timestamp(line) is { } time)
            {
                newest = time;
                if (time < activeSince) return (newest, false);
            }

            if (busy is null && line.Contains("\"event_msg\"", StringComparison.Ordinal))
            {
                switch (TurnEvent(line))
                {
                    case "task_started": busy = true; break;
                    case "task_complete" or "turn_aborted": busy = false; break;
                }
            }

            if (newest is not null && busy is not null) break;
        }

        // No turn event in the tail: a turn that has been producing output for a long time.
        return (newest, busy ?? true);
    }

    /// The top-level `timestamp` of an event line; null for a line without one or that doesn't parse (e.g. one Codex is
    /// still writing).
    static DateTimeOffset? Timestamp(string line)
    {
        if (!line.Contains("\"timestamp\"", StringComparison.Ordinal)) return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            return JsonFields.UnixSeconds(document.RootElement, "timestamp") is { } seconds
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? TurnEvent(string line)
    {
        if (!line.Contains("\"task_started\"", StringComparison.Ordinal) && !line.Contains("\"task_complete\"", StringComparison.Ordinal)
            && !line.Contains("\"turn_aborted\"", StringComparison.Ordinal))
            return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            return JsonFields.String(document.RootElement, "type") == "event_msg" && JsonFields.Object(document.RootElement, "payload") is { } payload
                ? JsonFields.String(payload, "type")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
