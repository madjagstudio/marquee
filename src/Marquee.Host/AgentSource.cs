namespace Marquee.Host;

/// One of the agent providers. config.yaml's `providers:` switches turn them on and off (AgentSources.Enabled).
public enum AgentSource
{
    /// Claude Code sessions, from its session files (`providers.claude`).
    ClaudeSessions,

    /// Claude Code limits from `claude -p /usage` (`providers.claude_usage`, `--no-claude-usage`).
    ClaudeUsage,

    /// Codex limits, from its rollout files (`providers.codex`).
    CodexLimits,

    /// Codex sessions, from its rollout files (`providers.codex`).
    CodexSessions,
}
