using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Codex;

/// Codex's usage limits, from the newest token_count event (by its own timestamp) in the recently written rollout logs.
/// Codex logs one after each model response, so they update while any Codex thread (CLI, desktop app, IDE) is working.
/// NTFS write times and sizes in directory listings lag for logs that stay open (by minutes), so they only choose which
/// logs to look at: whether a log changed is judged by its length from an open handle, and the newest event wins
/// whichever log it is in.
public sealed class CodexLimitsProvider(string folder, Func<DateTimeOffset>? now = null) : ISensorProvider
{
    /// Logs to look through at least, newest first: a brand-new thread's log may not have a token_count yet.
    const int MinFilesToSearch = 3;

    /// Logs written within this long are looked through as well, since an active log's write time lags behind.
    static readonly TimeSpan CandidateWindow = TimeSpan.FromHours(24);

    /// The most logs looked through, however many were written recently.
    const int MaxFilesToSearch = 16;

    /// How far back from the end of each log to look. A token_count follows every response, so it is near the end.
    const long BytesPerFile = 8 * 1024 * 1024;

    readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    /// Each log's newest token_count (or null for none), and the length it was read at. A log is read again only when
    /// its length changes.
    readonly Dictionary<string, (long Length, CodexLimits? Limits)> _read = new(StringComparer.OrdinalIgnoreCase);

    public CodexLimitsProvider() : this(AgentPaths.CodexSessions) { }

    public string Name => "Codex limits";
    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public void Poll(SensorStore store)
    {
        var now = _now();
        var candidateSince = (now - CandidateWindow).UtcDateTime;
        var files = RolloutFile.NewestFirst(folder)
            .TakeWhile((f, i) => i < MinFilesToSearch || f.LastWriteTimeUtc >= candidateSince)
            .Take(MaxFilesToSearch)
            .Select(f => f.FullName)
            .ToList();

        CodexLimits? newest = null;
        foreach (var path in files)
        {
            if (Limits(path) is not { } limits) continue;
            // Ties keep the log listed first (the most recently written).
            if (newest is null || (limits.At ?? double.MinValue) > (newest.At ?? double.MinValue)) newest = limits;
        }

        foreach (var gone in _read.Keys.Except(files, StringComparer.OrdinalIgnoreCase).ToList())
            _read.Remove(gone);

        if (newest is not null) CodexTokenCount.Apply(newest, store, now);
    }

    public void Dispose() { }

    /// The newest token_count in the log, read again only if the log's length changed since the last read. Null if it
    /// has none, or if it disappeared or is locked (the next poll tries again).
    CodexLimits? Limits(string path)
    {
        try
        {
            var length = RolloutFile.Length(path);
            if (_read.TryGetValue(path, out var known) && known.Length == length) return known.Limits;

            CodexLimits? limits = null;
            foreach (var line in RolloutFile.LinesFromEnd(path, BytesPerFile))
            {
                if (CodexTokenCount.TryParse(line) is not { } found) continue;
                limits = found;
                break;
            }

            _read[path] = (length, limits);
            return limits;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _read.Remove(path);
            return null;
        }
    }
}
