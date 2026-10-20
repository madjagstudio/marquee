using Marquee.Cli.Tests.TestSupport;
using Marquee.Host;

namespace Marquee.Cli.Tests;

/// `marquee run` while the tray app holds the screen. Each test holds a marker of its own name rather than the real
/// tray's, so a tray running on the developer's PC changes nothing. These capture the console, so they run in its
/// collection.
[Collection(nameof(ConsoleCapture))]
public class RunWhileTrayShowsTests
{
    static string UniqueName() => $@"Local\Marquee-Test-Screen-{Guid.NewGuid():N}";

    [Fact]
    public void Run_says_the_tray_has_the_screen_and_stops_before_touching_anything()
    {
        var marker = UniqueName();
        using var held = TrayScreen.Hold(marker);
        using var console = new ConsoleCapture();

        var exit = RunCommand.Run(["--config", "no-such-config.yaml"], marker);

        Assert.Equal(1, exit);
        Assert.EndsWith(
            "] Error: Marquee's tray app is showing the screen; pause it from the tray (or quit it) to use marquee run.",
            console.Error.Trim());
        Assert.Single(console.Error.Trim().Split(Environment.NewLine));
        Assert.Equal("", console.Out);
    }

    [Fact]
    public void Only_runs_that_need_the_screen_are_stopped()
    {
        var marker = UniqueName();
        using var held = TrayScreen.Hold(marker);

        Assert.True(RunCommand.TrayHasScreen(new RunOptions(), marker));
        Assert.True(RunCommand.TrayHasScreen(new RunOptions { Frames = 3 }, marker));
        // A sink folder needs no screen, so it runs beside the tray.
        Assert.False(RunCommand.TrayHasScreen(new RunOptions { Sink = "out" }, marker));
    }

    [Fact]
    public void With_no_tray_the_screen_is_free()
    {
        Assert.False(RunCommand.TrayHasScreen(new RunOptions(), UniqueName()));
    }

    [Fact]
    public void Bad_flags_are_still_a_usage_error_first()
    {
        var marker = UniqueName();
        using var held = TrayScreen.Hold(marker);

        Assert.Null(RunCommand.Run(["--unknown"], marker));
    }
}
