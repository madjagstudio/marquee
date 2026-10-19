using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

public class AgentSourcesTests
{
    [Theory]
    [InlineData(true, true, true, new[] { AgentSource.ClaudeSessions, AgentSource.ClaudeUsage, AgentSource.CodexLimits, AgentSource.CodexSessions })]
    [InlineData(false, true, true, new[] { AgentSource.ClaudeUsage, AgentSource.CodexLimits, AgentSource.CodexSessions })]
    [InlineData(true, false, true, new[] { AgentSource.ClaudeSessions, AgentSource.CodexLimits, AgentSource.CodexSessions })]
    [InlineData(true, true, false, new[] { AgentSource.ClaudeSessions, AgentSource.ClaudeUsage })]
    [InlineData(false, false, false, new AgentSource[0])]
    public void Enabled_lists_the_providers_the_switches_allow(bool claudeSessions, bool claudeUsage, bool codex, AgentSource[] expected)
    {
        Assert.Equal(expected, AgentSources.Enabled(claudeSessions, claudeUsage, codex));
    }

    [Fact]
    public void A_provider_that_cannot_be_built_is_logged_by_name_and_the_rest_still_start()
    {
        var log = new ListLog();

        var providers = AgentSources.Create(AgentSources.Enabled(), log, (source, _) => source == AgentSource.ClaudeUsage
            ? throw new InvalidOperationException("no claude.exe")
            : new FakeProvider(source.ToString()));

        Assert.Equal(["ClaudeSessions", "CodexLimits", "CodexSessions"], providers.Select(p => p.Name));
        Assert.Equal(["error: Claude Code limits is unavailable: no claude.exe"], log.Entries);
    }
}
