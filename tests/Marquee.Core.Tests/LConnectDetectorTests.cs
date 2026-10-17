using System.ComponentModel;
using System.ServiceProcess;
using Marquee.Core.Devices;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

public class LConnectDetectorTests
{
    [Theory]
    [InlineData(false, false, LConnectState.NotRunning)]
    [InlineData(false, true, LConnectState.AppOnly)]
    [InlineData(true, false, LConnectState.Service)]
    [InlineData(true, true, LConnectState.Service)]
    public void Detect_reports_the_service_ahead_of_the_app(bool service, bool app, LConnectState expected) =>
        Assert.Equal(expected, FakeLConnectStatus.Detector(service, app).Detect());

    [Fact]
    public void The_service_log_hint_says_closing_the_window_is_not_enough_and_how_to_hand_over()
    {
        var hint = LConnectDetector.LogHint(LConnectState.Service);

        Assert.Contains("L-Connect's background service is using it", hint);
        Assert.Contains("closing the L-Connect 3 window doesn't free it", hint);
        Assert.Contains("right-click L-Connect's tray icon and choose its stop-service option", hint);
        Assert.Contains("start the service again from the same menu once Marquee has the screen", hint);
        Assert.Contains("keeps running your fans and RGB", hint);
        Assert.DoesNotContain("quit it", hint);
        Assert.DoesNotContain('\n', hint);
    }

    [Fact]
    public void The_app_only_log_hint_stays_short() =>
        Assert.Equal("L-Connect 3 is running; quit it from its tray icon.", LConnectDetector.LogHint(LConnectState.AppOnly));

    [Fact]
    public void No_log_hint_when_L_Connect_is_not_running() =>
        Assert.Equal("", LConnectDetector.LogHint(LConnectState.NotRunning));

    [Fact]
    public void The_service_console_hint_names_the_service_and_the_tray_steps()
    {
        var text = string.Join("\n", LConnectDetector.ConsoleHint(LConnectState.Service));

        Assert.Contains("LConnectService", text);
        Assert.Contains("Closing the L-Connect 3 window doesn't free it.", text);
        Assert.Contains("right-click L-Connect's tray icon and choose its stop-service option, then try again", text);
        Assert.Contains("Start the service again from the same menu", text);
        Assert.Contains("L-Connect skips the screen and keeps running your fans and RGB", text);
        Assert.DoesNotContain("Quit it", text);
    }

    [Fact]
    public void The_app_only_console_hint_says_to_quit_from_the_tray() =>
        Assert.Equal(["L-Connect 3 is running. Quit it from its tray icon, then try again."], LConnectDetector.ConsoleHint(LConnectState.AppOnly));

    [Fact]
    public void No_console_hint_when_L_Connect_is_not_running() =>
        Assert.Empty(LConnectDetector.ConsoleHint(LConnectState.NotRunning));

    [Theory]
    [InlineData(ServiceControllerStatus.Running, true)]
    [InlineData(ServiceControllerStatus.StartPending, true)]
    [InlineData(ServiceControllerStatus.StopPending, true)]
    [InlineData(ServiceControllerStatus.Paused, true)]
    [InlineData(ServiceControllerStatus.PausePending, true)]
    [InlineData(ServiceControllerStatus.ContinuePending, true)]
    [InlineData(ServiceControllerStatus.Stopped, false)]
    public void Only_a_fully_stopped_service_counts_as_gone(ServiceControllerStatus status, bool holds)
    {
        var asked = new List<string>();
        var source = new SystemLConnectStatus(
            name => { asked.Add(name); return status; },
            _ => throw new InvalidOperationException("The process list shouldn't be needed."));

        Assert.Equal(holds, source.IsServiceRunning());
        Assert.Equal(["LConnectService"], asked);
    }

    [Fact]
    public void A_missing_service_falls_back_to_the_service_process()
    {
        var asked = new List<string>();
        var source = new SystemLConnectStatus(
            _ => throw new InvalidOperationException("Service LConnectService was not found on computer '.'."),
            name => { asked.Add(name); return true; });

        Assert.True(source.IsServiceRunning());
        Assert.Equal(["L-Connect-Service"], asked);
    }

    [Fact]
    public void An_SCM_error_falls_back_to_the_service_process()
    {
        var source = new SystemLConnectStatus(_ => throw new Win32Exception(5), _ => false);

        Assert.False(source.IsServiceRunning());
    }

    [Fact]
    public void The_app_is_found_by_its_process_name()
    {
        var asked = new List<string>();
        var source = new SystemLConnectStatus(_ => ServiceControllerStatus.Stopped, name => { asked.Add(name); return true; });

        Assert.True(source.IsAppRunning());
        Assert.Equal(["L-Connect 3"], asked);
    }

    [Fact]
    public void The_system_source_answers_without_admin_rights()
    {
        // Whatever is installed on this machine, asking must not throw (no L-Connect, no access to the SCM entry, etc.).
        var source = new SystemLConnectStatus();
        _ = source.IsServiceRunning();
        _ = source.IsAppRunning();
    }
}
