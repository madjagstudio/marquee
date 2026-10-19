using Marquee.Core.Config;
using Marquee.Core.Devices;

namespace Marquee.Core.Tests;

public class ConfigParserTests
{
    static ConfigException Error(string yaml) => Assert.Throws<ConfigException>(() => ConfigParser.Parse(yaml));

    [Fact]
    public void The_full_example_parses_every_key()
    {
        var config = ConfigParser.Parse("""
            theme: example-agents          # a theme folder name, or a full path
            orientation: landscape-flipped
            fps: 24
            sensors: hwinfo
            http: { port: 50000 }
            providers: { claude: on, claude_usage: off, codex: false, fps: true }
            custom: { max_keys: 250 }
            """);

        Assert.Equal(new MarqueeConfig
        {
            Theme = "example-agents",
            Orientation = Orientation.LandscapeFlipped,
            Fps = 24,
            Sensors = "hwinfo",
            HttpEnabled = true,
            HttpPort = 50000,
            ClaudeSessions = true,
            ClaudeUsage = false,
            Codex = false,
            GameFps = true,
            MaxCustomKeys = 250,
        }, config);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# just a comment\n")]
    [InlineData("# settings\n\n# more settings\n")]
    public void An_empty_file_means_all_defaults(string yaml) =>
        Assert.Equal(MarqueeConfig.Default, ConfigParser.Parse(yaml));

    [Fact]
    public void Defaults_leave_the_overridable_settings_unset_and_turn_everything_on()
    {
        var config = MarqueeConfig.Default;

        Assert.Null(config.Theme);
        Assert.Null(config.Orientation);
        Assert.Null(config.Fps);
        Assert.Null(config.Sensors);
        Assert.True(config.HttpEnabled);
        Assert.Null(config.HttpPort);
        Assert.True(config.ClaudeSessions);
        Assert.True(config.ClaudeUsage);
        Assert.True(config.Codex);
        Assert.True(config.GameFps);
        Assert.Equal(1000, config.MaxCustomKeys);
    }

    [Fact]
    public void Keys_left_out_of_a_section_keep_their_defaults()
    {
        var config = ConfigParser.Parse("providers: { codex: off }\nhttp: {}\ncustom: {}");

        Assert.Equal(MarqueeConfig.Default with { Codex = false }, config);
    }

    [Theory]
    [InlineData("portrait", Orientation.Portrait)]
    [InlineData("portrait-flipped", Orientation.PortraitFlipped)]
    [InlineData("landscape", Orientation.Landscape)]
    [InlineData("landscape-flipped", Orientation.LandscapeFlipped)]
    public void Orientation_takes_the_command_line_names(string name, Orientation expected) =>
        Assert.Equal(expected, ConfigParser.Parse($"orientation: {name}").Orientation);

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Switches_take_on_off_true_and_false(string word, bool expected)
    {
        var config = ConfigParser.Parse($"providers: {{ claude: {word}, claude_usage: {word}, codex: {word}, fps: {word} }}");

        Assert.Equal(expected, config.ClaudeSessions);
        Assert.Equal(expected, config.ClaudeUsage);
        Assert.Equal(expected, config.Codex);
        Assert.Equal(expected, config.GameFps);
    }

    [Fact]
    public void Http_off_turns_the_server_off()
    {
        var config = ConfigParser.Parse("http: off");

        Assert.False(config.HttpEnabled);
        Assert.Null(config.HttpPort);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(65535)]
    public void Http_port_accepts_the_ends_of_its_range(int port) =>
        Assert.Equal(port, ConfigParser.Parse($"http: {{ port: {port} }}").HttpPort);

    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public void Max_keys_accepts_the_ends_of_its_range(int max) =>
        Assert.Equal(max, ConfigParser.Parse($"custom: {{ max_keys: {max} }}").MaxCustomKeys);

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void Fps_accepts_the_ends_of_its_range(int fps) =>
        Assert.Equal(fps, ConfigParser.Parse($"fps: {fps}").Fps);

    [Theory]
    [InlineData("theme: \"\"", 1, "theme is empty")]
    [InlineData("theme: [a, b]", 1, "theme must be a single value, not a list or mapping")]
    [InlineData("fps: 30\norientation: sideways", 2,
        "orientation must be one of: portrait, portrait-flipped, landscape, landscape-flipped; not 'sideways'")]
    [InlineData("fps: 0", 1, "fps must be between 1 and 60")]
    [InlineData("fps: 61", 1, "fps must be between 1 and 60")]
    [InlineData("fps: 29.5", 1, "fps must be a whole number")]
    [InlineData("fps: fast", 1, "fps must be a number, not 'fast'")]
    [InlineData("theme: a\nsensors: aida64", 2, "sensors must be one of: lhm, hwinfo, none; not 'aida64'")]
    [InlineData("http: on", 1, "http must be off or a mapping like { port: 47913 }; not 'on'")]
    [InlineData("http: [47913]", 1, "http must be a mapping of keys to values")]
    [InlineData("http:\n  port: 1023", 2, "http.port must be between 1024 and 65535")]
    [InlineData("http:\n  port: 65536", 2, "http.port must be between 1024 and 65535")]
    [InlineData("http: { port: web }", 1, "http.port must be a number, not 'web'")]
    [InlineData("providers:\n  claude: on\n  codex: yes", 3, "providers.codex must be one of: on, off, true, false; not 'yes'")]
    [InlineData("providers: off", 1, "providers must be a mapping of keys to values")]
    [InlineData("custom:\n  max_keys: 0", 2, "custom.max_keys must be between 1 and 10000")]
    [InlineData("custom:\n  max_keys: 10001", 2, "custom.max_keys must be between 1 and 10000")]
    [InlineData("custom: { max_keys: lots }", 1, "custom.max_keys must be a number, not 'lots'")]
    [InlineData("landscape", 1, "config.yaml must be a mapping of keys to values")]
    [InlineData("fps: 30\nfps: 60", 2, "YAML syntax error: Duplicate key fps")]
    public void Bad_values_name_the_line_and_the_problem(string yaml, int line, string reason)
    {
        var ex = Error(yaml);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Theory]
    [InlineData("theme: a\nthemes: b", 2, "Unknown key 'themes' in config.yaml. Expected one of: theme, orientation, fps, sensors, http, providers, custom")]
    [InlineData("ring: off", 1, "Unknown key 'ring' in config.yaml. Expected one of: theme, orientation, fps, sensors, http, providers, custom")]
    [InlineData("providers:\n  claude: on\n  gpu: on", 3, "Unknown key 'gpu' in providers. Expected one of: claude, claude_usage, codex, fps")]
    [InlineData("custom:\n  max: 5", 2, "Unknown key 'max' in custom. Expected one of: max_keys")]
    [InlineData("http: { port: 47913, host: 0.0.0.0 }", 1, "Unknown key 'host' in http. Expected one of: port")]
    public void Unknown_keys_are_errors_with_their_line(string yaml, int line, string reason)
    {
        var ex = Error(yaml);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Fact]
    public void Syntax_errors_name_config_yaml_and_the_line()
    {
        var ex = Error("""
            theme: example-agents
            http:
              port: 47913
             fps: 30
            """);

        Assert.StartsWith("YAML syntax error:", ex.Reason);
        Assert.Equal(4, ex.Line);
        Assert.StartsWith("config.yaml line 4: YAML syntax error:", ex.Message);
    }

    [Fact]
    public void An_unclosed_flow_list_names_config_yaml_not_the_theme_file()
    {
        // YamlDotNet reports an unclosed list followed by another line without a position, so the file name has to be
        // in the reason itself.
        var ex = Error("providers: [claude, codex\nfps: 30");

        Assert.StartsWith("YAML syntax error in config.yaml:", ex.Reason);
        Assert.Equal(0, ex.Line);
        Assert.DoesNotContain("layout.yaml", ex.Message);
    }

    [Fact]
    public void Message_leads_with_file_and_line() =>
        Assert.Equal("config.yaml line 1: fps must be between 1 and 60", Error("fps: 0").Message);

    [Fact]
    public void Message_is_the_reason_alone_without_a_line() =>
        Assert.Equal("Something is wrong", new ConfigException("Something is wrong").Message);
}
