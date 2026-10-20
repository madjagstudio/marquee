using System.ServiceProcess;
using Marquee.Cli.Tests.TestSupport;
using Marquee.Host;

namespace Marquee.Cli.Tests;

/// `marquee claim-screen`, with the elevation check and L-Connect's services faked, and the screen signal under a name
/// of each test's own: the test plays the running Marquee by making the signal and setting it once L-Connect's service
/// has stopped. No test needs elevation or touches a real service. These capture the console, so they run in its
/// collection.
[Collection(nameof(ConsoleCapture))]
public sealed class ClaimScreenCommandTests
{
    const string Service = FakeServiceControl.Service;
    const string Watcher = FakeServiceControl.Watcher;

    readonly string _signalName = $@"Local\Marquee.Tests.ScreenOpen.{Guid.NewGuid():N}";
    readonly FakeServiceControl _services = new();

    (int? Exit, string Out, string Error) Run(bool elevated, params string[] args)
    {
        using var console = new ConsoleCapture();
        var exit = ClaimScreenCommand.Run(args, new ClaimScreenCommand.Machine(() => elevated, _services, _signalName));
        return (exit, console.Out.Trim(), console.Error.Trim());
    }

    [Fact]
    public void It_needs_an_administrator_terminal()
    {
        using var marquee = ScreenSignal.Create(_signalName);

        var (exit, _, error) = Run(elevated: false);

        Assert.Equal(1, exit);
        Assert.StartsWith("`marquee claim-screen` needs an administrator terminal, because stopping L-Connect's service", error);
        Assert.Empty(_services.Calls);
    }

    [Fact]
    public void With_no_Marquee_running_it_leaves_L_Connect_alone()
    {
        var (exit, output, error) = Run(elevated: true);

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Equal(
            "No Marquee is running to take the screen. Start `marquee run` (or the tray) first, then run `marquee claim-screen` again.",
            error);
        Assert.Empty(_services.Calls);
    }

    [Fact]
    public void A_Marquee_that_has_the_screen_already_needs_nothing()
    {
        using var marquee = ScreenSignal.Create(_signalName)!;
        marquee.Set();

        var (exit, output, _) = Run(elevated: true);

        Assert.Equal(0, exit);
        Assert.Equal("Marquee already has the screen.", output);
        Assert.Empty(_services.Calls);
    }

    [Fact]
    public void It_stops_L_Connect_until_Marquee_has_the_screen_then_starts_it_again()
    {
        using var marquee = ScreenSignal.Create(_signalName)!;
        // The running Marquee opens the screen at its next retry once L-Connect's service has stopped.
        _services.Stopped = name =>
        {
            if (name == Service) marquee.Set();
        };

        var (exit, output, error) = Run(elevated: true);

        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Equal(
            [$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"wait {Service} Stopped", $"start {Service}", $"start {Watcher}"],
            _services.Calls);
        var lines = output.Split(Environment.NewLine);
        Assert.Contains(lines, line => line.EndsWith("] Marquee has the screen.", StringComparison.Ordinal));
        Assert.Equal("Marquee has the screen, and L-Connect is running your fans and RGB again.", lines[^1]);
        Assert.Equal(ServiceControllerStatus.Running, _services.Status(Service));
    }

    [Fact]
    public void A_handover_that_fails_says_why_and_exits_1()
    {
        using var marquee = ScreenSignal.Create(_signalName);
        _services.FailToStop = true;

        var (exit, _, error) = Run(elevated: true);

        Assert.Equal(1, exit);
        Assert.Contains("Error: Couldn't take the screen from L-Connect: Cannot open LConnectServiceWatcher service on computer '.'.", error);
        Assert.Equal(ServiceControllerStatus.Running, _services.Status(Service));
    }

    [Fact]
    public void With_L_Connect_s_service_not_running_there_is_nothing_to_take()
    {
        using var marquee = ScreenSignal.Create(_signalName);
        _services.SetStatus(Service, ServiceControllerStatus.Stopped);

        var (exit, output, _) = Run(elevated: true);

        Assert.Equal(1, exit);
        Assert.EndsWith("] L-Connect's service isn't running, so there's no screen to take from it.", output);
        Assert.Empty(_services.Calls);
    }

    [Fact]
    public void It_takes_no_options()
    {
        var (exit, _, _) = Run(elevated: true, "--now");

        Assert.Null(exit);
        Assert.Empty(_services.Calls);
    }
}
