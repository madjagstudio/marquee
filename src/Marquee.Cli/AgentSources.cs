using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Codex;

/// The agent providers: Claude Code sessions, Codex limits and sessions from files, and Claude Code limits from `claude -p /usage`.
/// The status-line pipe (StatusLineServer) also delivers Claude Code limits, from sessions that run a status line.
static class AgentSources
{
    /// Builds each provider on its own, so one that can't start is logged and skipped rather than stopping the rest.
    public static IReadOnlyList<ISensorProvider> Create(ILog log, bool claudeUsage = true)
    {
        var providers = new List<ISensorProvider>();
        Add(providers, log, Strings.ClaudeSessionsSource, () => new ClaudeSessionsProvider());
        if (claudeUsage) Add(providers, log, Strings.ClaudeLimitsSource, () => new ClaudeUsageProvider(log));
        Add(providers, log, Strings.CodexLimitsSource, () => new CodexLimitsProvider());
        Add(providers, log, Strings.CodexSessionsSource, () => new CodexSessionsProvider());
        return providers;
    }

    static void Add(List<ISensorProvider> providers, ILog log, string name, Func<ISensorProvider> build)
    {
        try
        {
            providers.Add(build());
        }
        catch (Exception ex)
        {
            log.Error(Strings.SourceUnavailable(name, ex.Message));
        }
    }
}
