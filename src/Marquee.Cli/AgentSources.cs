using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Codex;

/// The file-based agent providers. Claude Code's limits arrive through the status-line pipe instead (StatusLineServer).
static class AgentSources
{
    public static IReadOnlyList<ISensorProvider> Create() =>
        [new ClaudeSessionsProvider(), new CodexLimitsProvider(), new CodexSessionsProvider()];
}
