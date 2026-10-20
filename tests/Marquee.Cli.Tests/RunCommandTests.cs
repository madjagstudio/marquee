using Marquee.Cli.Tests.TestSupport;
using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Host;

namespace Marquee.Cli.Tests;

public class RunCommandTests
{
    [Fact]
    public void The_theme_folder_is_optional_so_config_yaml_can_name_it()
    {
        Assert.Equal(new RunOptions(), RunCommand.Parse([]));
        Assert.Equal(new RunOptions { Fps = 20 }, RunCommand.Parse(["--fps", "20"]));
        Assert.Equal(new RunOptions { Theme = "themes/example-agents", Fps = 20 }, RunCommand.Parse(["themes/example-agents", "--fps", "20"]));
    }

    [Fact]
    public void Every_flag_lands_in_the_options()
    {
        var options = RunCommand.Parse([
            "neon", "--orientation", "portrait-flipped", "--fps", "60", "--sensors", "none", "--sink", "out", "--frames", "3",
            "--preview", "--http-port", "50000", "--no-claude-usage", "--no-fps", "--config", "my.yaml",
        ]);

        Assert.Equal(new RunOptions
        {
            Theme = "neon",
            Orientation = Orientation.PortraitFlipped,
            Fps = 60,
            Sensors = "none",
            Sink = "out",
            Frames = 3,
            Preview = true,
            HttpEnabled = true,
            HttpPort = 50000,
            ClaudeUsage = false,
            GameFps = false,
            ConfigFile = "my.yaml",
        }, options);
    }

    [Fact]
    public void No_http_turns_the_server_off()
    {
        Assert.Equal(new RunOptions { HttpEnabled = false }, RunCommand.Parse(["--no-http"]));
    }

    /// A theme, and beside it a config.yaml standing in for the user's %LOCALAPPDATA%\Marquee\config.yaml, which says
    /// portrait at 5 fps.
    sealed class Profile : IDisposable
    {
        public TempTheme Theme { get; } = new("canvas: { orientation: landscape }\n");

        public Profile() => Theme.AddFile(@"profile\config.yaml", "orientation: portrait\nfps: 5\n");

        public string UserConfig => Path.Combine(Theme.Folder, "profile", "config.yaml");

        /// No theme folders: a name would not be looked up in the user's themes either.
        public ThemeLocator Locator => new([], Theme.Folder);

        public void Dispose() => Theme.Dispose();
    }

    sealed class RecordingLog : ILog
    {
        public List<string> Entries { get; } = [];
        public void Info(string message) => Entries.Add(message);
        public void Warn(string message) => Entries.Add(message);
        public void Error(string message) => Entries.Add(message);
    }

    static SettingsResult Resolve(Profile profile, RecordingLog log, params string[] args)
    {
        var options = RunCommand.Parse(args)!;
        return RunCommand.Resolve(options, RunCommand.ConfigToRead(options, profile.UserConfig), profile.Locator, log);
    }

    [Fact]
    public void A_sink_run_reads_config_yaml_only_when_config_names_one()
    {
        const string user = @"C:\Users\someone\AppData\Local\Marquee\config.yaml";

        Assert.Equal(user, RunCommand.ConfigToRead(new RunOptions(), user));
        Assert.Equal(Path.GetFullPath("my.yaml"), RunCommand.ConfigToRead(new RunOptions { ConfigFile = "my.yaml" }, user));
        Assert.Null(RunCommand.ConfigToRead(new RunOptions { Sink = "out" }, user));
        Assert.Equal(Path.GetFullPath("my.yaml"), RunCommand.ConfigToRead(new RunOptions { Sink = "out", ConfigFile = "my.yaml" }, user));
    }

    [Fact]
    public void A_sink_run_leaves_the_users_settings_out_of_its_frames()
    {
        using var profile = new Profile();
        var log = new RecordingLog();

        var screen = Resolve(profile, log, profile.Theme.Folder);
        var sink = Resolve(profile, log, profile.Theme.Folder, "--sink", "out", "--frames", "1");

        Assert.Equal((Orientation.Portrait, 5), (screen.Settings!.Orientation, screen.Settings.FpsOverride));
        Assert.Equal((Orientation.Landscape, null), (sink.Settings!.Orientation, sink.Settings.FpsOverride));
        Assert.Equal("out", sink.Settings.Sink);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void A_sink_run_reads_the_file_config_names()
    {
        using var profile = new Profile();
        var log = new RecordingLog();

        var sink = Resolve(profile, log, profile.Theme.Folder, "--sink", "out", "--config", profile.UserConfig);

        Assert.Equal(Orientation.Portrait, sink.Settings!.Orientation);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void A_sink_run_with_no_theme_says_config_yaml_is_not_read()
    {
        using var profile = new Profile();
        profile.Theme.AddFile(@"profile\config.yaml", $"theme: {profile.Theme.Folder}\n");

        var sink = Resolve(profile, new RecordingLog(), "--sink", "out");

        Assert.Equal(
            "A --sink run reads no config.yaml unless --config names one, so pass a theme folder to marquee run.",
            sink.Error);
    }

    [Theory]
    [InlineData("--fps", "0")]
    [InlineData("--fps", "61")]
    [InlineData("--sensors", "aida64")]
    [InlineData("--orientation", "sideways")]
    [InlineData("--http-port", "80")]
    [InlineData("--frames", "0")]
    [InlineData("--config")]
    [InlineData("--unknown")]
    public void A_bad_flag_means_usage(params string[] args)
    {
        Assert.Null(RunCommand.Parse(args));
    }
}
