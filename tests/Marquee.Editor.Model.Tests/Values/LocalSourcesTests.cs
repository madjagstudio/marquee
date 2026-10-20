using Marquee.Core.Config;
using Marquee.Core.Sensors;
using Marquee.Editor.Model.Tests.TestSupport;
using Marquee.Editor.Model.Values;
using Marquee.Host;

namespace Marquee.Editor.Model.Tests.Values;

/// The editor reads only what config.yaml lets the tray read: its hardware source (`sensors:`) and the agent providers
/// its `providers:` switches leave on, never Claude's `/usage`. Nothing here starts a provider.
public class LocalSourcesTests
{
    static LocalSources Of(string yaml) => LocalSources.Of(ConfigParser.Parse(yaml));

    [Fact]
    public void Without_a_config_it_reads_lhm_and_the_agents_but_not_claudes_usage()
    {
        var sources = Of("");

        Assert.Equal(SensorSourceNames.LibreHardwareMonitor, sources.Hardware);
        Assert.Equal([AgentSource.ClaudeSessions, AgentSource.CodexLimits, AgentSource.CodexSessions], sources.Agents);
    }

    [Theory]
    [InlineData("sensors: none\n", null)]
    [InlineData("sensors: hwinfo\n", SensorSourceNames.HwInfo)]
    [InlineData("sensors: lhm\n", SensorSourceNames.LibreHardwareMonitor)]
    public void The_hardware_source_is_config_yamls(string yaml, string? hardware) => Assert.Equal(hardware, Of(yaml).Hardware);

    [Theory]
    [InlineData("providers: { claude: off }\n", new[] { AgentSource.CodexLimits, AgentSource.CodexSessions })]
    [InlineData("providers: { codex: off }\n", new[] { AgentSource.ClaudeSessions })]
    [InlineData("providers: { claude: off, codex: off }\n", new AgentSource[0])]
    [InlineData("providers: { claude_usage: on }\n", new[] { AgentSource.ClaudeSessions, AgentSource.CodexLimits, AgentSource.CodexSessions })]
    public void The_agents_are_those_its_switches_leave_on(string yaml, AgentSource[] agents) => Assert.Equal(agents, Of(yaml).Agents);

    [Fact]
    public void The_value_sources_for_this_user_read_the_config_file_they_are_given()
    {
        using var folder = new TempFolder();
        var config = folder.Combine(ConfigParser.FileName);
        File.WriteAllText(config, "sensors: none\nproviders: { codex: off }\n");

        using var sources = ValueSources.ForThisUser(config);

        var local = ((LocalReadings)sources.Local).Sources!;
        Assert.Null(local.Hardware);
        Assert.Equal([AgentSource.ClaudeSessions], local.Agents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sensors: [broken\n")]
    public void A_missing_or_broken_config_file_reads_as_the_defaults(string? yaml)
    {
        using var folder = new TempFolder();
        var config = folder.Combine(ConfigParser.FileName);
        if (yaml is not null) File.WriteAllText(config, yaml);

        using var sources = ValueSources.ForThisUser(config);

        var local = ((LocalReadings)sources.Local).Sources!;
        Assert.Equal(SensorSourceNames.LibreHardwareMonitor, local.Hardware);
        Assert.Equal(LocalSources.Of(MarqueeConfig.Default).Agents, local.Agents);
    }
}
