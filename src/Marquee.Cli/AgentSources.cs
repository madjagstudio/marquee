using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Codex;

/// The file-based agent providers. Claude Code's limits arrive through the status-line pipe instead (StatusLineServer).
static class AgentSources
{
    /// Builds each provider on its own, so one that can't start is logged and skipped rather than stopping the rest.
    public static IReadOnlyList<ISensorProvider> Create(ILog log)
    {
        var providers = new List<ISensorProvider>();
        Add(providers, log, "Claude Code sessions", () => new ClaudeSessionsProvider());
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
