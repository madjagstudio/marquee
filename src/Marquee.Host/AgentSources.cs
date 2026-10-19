using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Host.Resources;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Codex;

namespace Marquee.Host;

/// The agent providers: Claude Code sessions, Codex limits and sessions from files, and Claude Code limits from `claude -p /usage`.
/// The status-line pipe (StatusLineServer) also delivers Claude Code limits, from sessions that run a status line.
public static class AgentSources
{
    /// The providers the switches allow, in the order they start. `codex` covers both Codex providers.
    public static IReadOnlyList<AgentSource> Enabled(bool claudeSessions = true, bool claudeUsage = true, bool codex = true)
    {
        var sources = new List<AgentSource>();
        if (claudeSessions) sources.Add(AgentSource.ClaudeSessions);
        if (claudeUsage) sources.Add(AgentSource.ClaudeUsage);
        if (codex)
        {
            sources.Add(AgentSource.CodexLimits);
            sources.Add(AgentSource.CodexSessions);
        }

        return sources;
    }

    /// The real provider for a source.
    public static ISensorProvider Build(AgentSource source, ILog log) => source switch
    {
        AgentSource.ClaudeSessions => new ClaudeSessionsProvider(),
        AgentSource.ClaudeUsage => new ClaudeUsageProvider(log),
        AgentSource.CodexLimits => new CodexLimitsProvider(),
        AgentSource.CodexSessions => new CodexSessionsProvider(),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Not an agent source"),
    };

    /// Builds each provider on its own, so one that can't start is logged and skipped rather than stopping the rest.
    /// `build` is Build unless a test or the engine's services make them another way.
    public static IReadOnlyList<ISensorProvider> Create(IEnumerable<AgentSource> sources, ILog log,
        Func<AgentSource, ILog, ISensorProvider>? build = null)
    {
        build ??= Build;
        var providers = new List<ISensorProvider>();
        foreach (var source in sources)
        {
            try
            {
                providers.Add(build(source, log));
            }
            catch (Exception ex)
            {
                log.Error(Strings.SourceUnavailable(Name(source), ex.Message));
            }
        }

        return providers;
    }

    /// The source's name in messages.
    public static string Name(AgentSource source) => source switch
    {
        AgentSource.ClaudeSessions => Strings.ClaudeSessionsSource,
        AgentSource.ClaudeUsage => Strings.ClaudeLimitsSource,
        AgentSource.CodexLimits => Strings.CodexLimitsSource,
        AgentSource.CodexSessions => Strings.CodexSessionsSource,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Not an agent source"),
    };
}
