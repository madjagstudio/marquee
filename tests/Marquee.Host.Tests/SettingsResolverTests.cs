using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

/// Each setting is checked three ways: a flag beats config.yaml, config.yaml beats the default, and the default.
public class SettingsResolverTests : IDisposable
{
    readonly TempDir _themes = new();
    readonly ThemeLocator _locator;

    public SettingsResolverTests()
    {
        _themes.Folder("from-flag");
        _themes.Folder("from-config");
        _locator = new ThemeLocator([_themes.Path], _themes.Path);
    }

    public void Dispose() => _themes.Dispose();

    /// A config.yaml that names a theme and sets nothing else, so each test changes one thing.
    static readonly MarqueeConfig Config = new() { Theme = "from-config" };

    /// Where Config was read from: beside the themes, which are also the current directory here.
    string ConfigFile => _themes.File("config.yaml");

    EngineSettings Resolve(RunOptions options, MarqueeConfig config)
    {
        var result = SettingsResolver.Resolve(options, config, _locator, ConfigFile);
        Assert.True(result.Succeeded, result.Error);
        return result.Settings;
    }

    [Fact]
    public void Theme_flag_beats_config()
    {
        Assert.Equal(_themes.File("from-flag"), Resolve(new RunOptions { Theme = "from-flag" }, Config).ThemeFolder);
        Assert.Equal(_themes.File("from-config"), Resolve(new RunOptions(), Config).ThemeFolder);
    }

    [Fact]
    public void No_theme_anywhere_is_an_error_that_says_how_to_give_one()
    {
        var result = SettingsResolver.Resolve(new RunOptions(), MarqueeConfig.Default, _locator, ConfigFile);

        Assert.False(result.Succeeded);
        Assert.Equal("No theme to show. Pass a theme folder to `marquee run`, or set theme: in config.yaml.", result.Error);
    }

    [Fact]
    public void A_theme_that_is_not_found_is_the_locators_error()
    {
        var result = SettingsResolver.Resolve(new RunOptions(), new MarqueeConfig { Theme = "missing" }, _locator, ConfigFile);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Theme 'missing' was not found.", result.Error);
    }

    [Fact]
    public void A_theme_flag_is_used_even_when_the_config_theme_is_missing()
    {
        var settings = Resolve(new RunOptions { Theme = "from-flag" }, new MarqueeConfig { Theme = "missing" });

        Assert.Equal(_themes.File("from-flag"), settings.ThemeFolder);
    }

    [Theory]
    [InlineData(@".\mine")]
    [InlineData("mine")]
    public void A_relative_theme_in_config_is_found_from_the_config_files_folder(string theme)
    {
        // The tray starts in System32 (Task Scheduler's working folder), so config.yaml can't mean the current one.
        using var root = new TempDir();
        var work = root.Folder("work");
        root.Folder(@"work\mine");
        var beside = root.Folder(@"settings\mine");
        var locator = new ThemeLocator([root.Folder("themes")], work);

        var result = SettingsResolver.Resolve(new RunOptions(), new MarqueeConfig { Theme = theme }, locator, root.File(@"settings\config.yaml"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(beside, result.Settings.ThemeFolder);
    }

    [Fact]
    public void A_relative_theme_on_the_command_line_is_found_from_the_current_folder()
    {
        using var root = new TempDir();
        var work = root.Folder("work");
        var current = root.Folder(@"work\mine");
        root.Folder(@"settings\mine");
        var locator = new ThemeLocator([root.Folder("themes")], work);

        var result = SettingsResolver.Resolve(new RunOptions { Theme = @".\mine" }, MarqueeConfig.Default, locator, root.File(@"settings\config.yaml"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(current, result.Settings.ThemeFolder);
    }

    [Fact]
    public void Orientation_flag_beats_config_beats_landscape()
    {
        var config = Config with { Orientation = Orientation.Portrait };

        Assert.Equal(Orientation.LandscapeFlipped, Resolve(new RunOptions { Orientation = Orientation.LandscapeFlipped }, config).Orientation);
        Assert.Equal(Orientation.Portrait, Resolve(new RunOptions(), config).Orientation);
        Assert.Equal(Orientation.Landscape, Resolve(new RunOptions(), Config).Orientation);
    }

    [Fact]
    public void Fps_flag_beats_config_beats_the_themes_own()
    {
        var config = Config with { Fps = 24 };

        Assert.Equal(60, Resolve(new RunOptions { Fps = 60 }, config).FpsOverride);
        Assert.Equal(24, Resolve(new RunOptions(), config).FpsOverride);
        Assert.Null(Resolve(new RunOptions(), Config).FpsOverride);
    }

    [Fact]
    public void Sensors_flag_beats_config_beats_lhm()
    {
        var config = Config with { Sensors = "hwinfo" };

        Assert.Equal("none", Resolve(new RunOptions { Sensors = "none" }, config).Sensors);
        Assert.Equal("hwinfo", Resolve(new RunOptions(), config).Sensors);
        Assert.Equal("lhm", Resolve(new RunOptions(), Config).Sensors);
    }

    [Fact]
    public void Http_port_flag_beats_config_beats_the_default_port()
    {
        var config = Config with { HttpPort = 50000 };

        Assert.Equal(50001, Resolve(new RunOptions { HttpEnabled = true, HttpPort = 50001 }, config).HttpPort);
        Assert.Equal(50000, Resolve(new RunOptions(), config).HttpPort);
        Assert.Equal(47913, Resolve(new RunOptions(), Config).HttpPort);
    }

    [Fact]
    public void No_http_flag_beats_a_config_port()
    {
        Assert.Null(Resolve(new RunOptions { HttpEnabled = false }, Config with { HttpPort = 50000 }).HttpPort);
    }

    [Fact]
    public void Http_off_in_config_turns_the_server_off_unless_a_port_flag_turns_it_on()
    {
        var config = Config with { HttpEnabled = false };

        Assert.Null(Resolve(new RunOptions(), config).HttpPort);
        Assert.Equal(50001, Resolve(new RunOptions { HttpEnabled = true, HttpPort = 50001 }, config).HttpPort);
    }

    [Fact]
    public void Claude_usage_flag_beats_config_beats_on()
    {
        var off = Config with { ClaudeUsage = false };

        Assert.False(Resolve(new RunOptions { ClaudeUsage = false }, Config).ClaudeUsage);
        Assert.False(Resolve(new RunOptions(), off).ClaudeUsage);
        Assert.True(Resolve(new RunOptions(), Config).ClaudeUsage);
    }

    [Fact]
    public void Fps_flag_beats_config_beats_on()
    {
        var off = Config with { GameFps = false };

        Assert.False(Resolve(new RunOptions { GameFps = false }, Config).GameFps);
        Assert.False(Resolve(new RunOptions(), off).GameFps);
        Assert.True(Resolve(new RunOptions(), Config).GameFps);
    }

    [Fact]
    public void Settings_without_a_flag_come_from_config_or_their_default()
    {
        var config = Config with { ClaudeSessions = false, Codex = false, GameFps = false, MaxCustomKeys = 250 };

        var set = Resolve(new RunOptions(), config);
        var unset = Resolve(new RunOptions(), Config);

        Assert.Equal((false, false, false, 250), (set.ClaudeSessions, set.Codex, set.GameFps, set.MaxCustomKeys));
        Assert.Equal((true, true, true, 1000), (unset.ClaudeSessions, unset.Codex, unset.GameFps, unset.MaxCustomKeys));
    }

    [Fact]
    public void Sink_comes_only_from_the_command_line()
    {
        Assert.Equal("frames", Resolve(new RunOptions { Sink = "frames" }, Config).Sink);
        Assert.Null(Resolve(new RunOptions(), Config).Sink);
    }

    [Fact]
    public void Nothing_but_a_theme_gives_the_engines_defaults()
    {
        Assert.Equal(new EngineSettings { ThemeFolder = _themes.File("from-config") }, Resolve(new RunOptions(), Config));
    }
}
