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
        Add(providers, log, "Claude Code sessions", () => new ClaudeSessionsProvider());
        if (claudeUsage) Add(providers, log, "Claude Code limits", () => new ClaudeUsageProvider(log));
        Add(providers, log, "Codex limits", () => new CodexLimitsProvider());
        Add(providers, log, "Codex sessions", () => new CodexSessionsProvider());
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
            log.Error($"{name} is unavailable: {ex.Message}");
        }
    }
}
