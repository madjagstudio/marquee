using Marquee.Core.Devices;
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
            "--preview", "--http-port", "50000", "--no-claude-usage", "--config", "my.yaml",
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
            ConfigFile = "my.yaml",
        }, options);
    }

    [Fact]
    public void No_http_turns_the_server_off()
    {
        Assert.Equal(new RunOptions { HttpEnabled = false }, RunCommand.Parse(["--no-http"]));
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
