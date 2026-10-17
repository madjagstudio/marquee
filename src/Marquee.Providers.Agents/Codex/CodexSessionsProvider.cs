using System.Text.Json;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Codex;

/// Counts Codex sessions as the rollout logs written in the last 10 minutes: codex.sessions.total, .busy and .idle.
/// Running processes can't be used: the Codex desktop app runs every thread inside one app-server process. Logs of
/// sub-agents and reviews (session_meta with a parent_thread_id) belong to a session already counted, so they're skipped.
/// A session is busy when its last turn event is task_started, idle after task_complete or turn_aborted.
public sealed class CodexSessionsProvider(string folder, Func<DateTimeOffset>? now = null) : ISensorProvider
{
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(10);

    /// session_meta, the first line of each log, is about 20 KB.
    const int FirstLineBytes = 256 * 1024;

    /// How far back to look for the last turn event. Further back means a turn that has been working for a long time.
    const long TailBytes = 1024 * 1024;

    static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    /// Whether each log is a sub-agent's. A log's first line never changes, so it is read once.
    readonly Dictionary<string, bool> _subagent = new(StringComparer.OrdinalIgnoreCase);

    public CodexSessionsProvider() : this(AgentPaths.CodexSessions) { }

    public string Name => "Codex sessions";
    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public void Poll(SensorStore store)
    {
        var since = (_now() - ActiveWindow).UtcDateTime;
        var recent = RolloutFile.NewestFirst(folder).TakeWhile(f => f.LastWriteTimeUtc >= since).Select(f => f.FullName).ToList();

        int busy = 0, idle = 0;
        foreach (var path in recent)
        {
            try
            {
                if (IsSubagent(path)) continue;
                if (IsBusy(path)) busy++;
                else idle++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Deleted or locked since it was listed; the next poll sees it as it is then.
            }
        }

        foreach (var gone in _subagent.Keys.Except(recent, StringComparer.OrdinalIgnoreCase).ToList())
            _subagent.Remove(gone);

        store.Set("codex.sessions.total", busy + idle, Ttl);
        store.Set("codex.sessions.busy", busy, Ttl);
        store.Set("codex.sessions.idle", idle, Ttl);
    }

    public void Dispose() { }

    bool IsSubagent(string path)
    {
        if (!_subagent.TryGetValue(path, out var subagent))
            _subagent[path] = subagent = HasParent(RolloutFile.FirstLine(path, FirstLineBytes));
        return subagent;
    }

    /// True for a session_meta line naming a parent thread. Anything unreadable counts as a session of its own.
    static bool HasParent(string? firstLine)
    {
        if (firstLine is null) return false;
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

    static bool IsBusy(string path)
    {
        foreach (var line in RolloutFile.LinesFromEnd(path, TailBytes))
        {
            if (!line.Contains("\"event_msg\"", StringComparison.Ordinal)) continue;
            switch (TurnEvent(line))
            {
                case "task_started": return true;
                case "task_complete" or "turn_aborted": return false;
            }
        }

        return true;
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
