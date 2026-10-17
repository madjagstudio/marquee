using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Codex;

/// Codex's usage limits, from the newest token_count event in the most recently written rollout logs. Codex logs one
/// after each model response, so they update while any Codex thread (CLI, desktop app, IDE) is working.
public sealed class CodexLimitsProvider(string folder, Func<DateTimeOffset>? now = null) : ISensorProvider
{
    /// Logs to look through, newest first: a brand-new thread's log may not have a token_count yet.
    const int FilesToSearch = 3;

    /// How far back from the end of each log to look. A token_count follows every response, so it is near the end.
    const long BytesPerFile = 8 * 1024 * 1024;

    readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    (string Path, long Length, DateTime Written)[]? _searched;

    public CodexLimitsProvider() : this(AgentPaths.CodexSessions) { }

    public string Name => "Codex limits";
    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public void Poll(SensorStore store)
    {
        var files = RolloutFile.NewestFirst(folder).Take(FilesToSearch).ToList();
        var signature = files.Select(f => (f.FullName, f.Length, f.LastWriteTimeUtc)).ToArray();
        if (_searched is not null && signature.SequenceEqual(_searched)) return; // nothing new since the last search

        foreach (var file in files)
        {
            foreach (var line in RolloutFile.LinesFromEnd(file.FullName, BytesPerFile))
            {
                if (CodexTokenCount.TryParse(line) is not { } limits) continue;
                CodexTokenCount.Apply(limits, store, _now());
                _searched = signature;
                return;
            }
        }

        _searched = signature;
    }

    public void Dispose() { }
}
