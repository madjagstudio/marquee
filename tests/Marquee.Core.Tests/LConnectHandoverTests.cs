using System.ServiceProcess;
using Marquee.Core.Devices;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

public class LConnectHandoverTests
{
    const string Service = "LConnectService";
    const string Watcher = "LConnectServiceWatcher";
    const ServiceControllerStatus Running = ServiceControllerStatus.Running;
    const ServiceControllerStatus Stopped = ServiceControllerStatus.Stopped;

    static FakeServiceControl BothRunning() => new((Service, Running), (Watcher, Running));

    /// The engine's side: records that it was asked to open the screen, and answers.
    static Func<TimeSpan, bool> Screen(FakeServiceControl services, bool opens = true) => _ =>
    {
        services.Calls.Add("screen");
        return opens;
    };

    [Fact]
    public void Stops_the_watcher_then_the_service_opens_the_screen_and_starts_the_service_then_the_watcher()
    {
        var services = BothRunning();

        var result = new LConnectHandover(services, new ListLog()).TakeScreen(Screen(services));

        Assert.Equal(
            [$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"wait {Service} Stopped", "screen", $"start {Service}", $"start {Watcher}"],
            services.Calls);
        Assert.Equal(HandoverOutcome.Taken, result.Outcome);
        Assert.Equal("Marquee has the screen, and L-Connect is running your fans and RGB again.", result.Message);
        Assert.Equal([Service, Watcher], result.Restarted);
        Assert.Empty(result.RestartFailed);
        Assert.Equal(Running, services.Status(Service));
        Assert.Equal(Running, services.Status(Watcher));
    }

    [Fact]
    public void Waits_15_seconds_for_the_watcher_and_the_service_to_stop_and_for_the_screen()
    {
        var services = BothRunning();
        var screenTimeouts = new List<TimeSpan>();

        new LConnectHandover(services, new ListLog()).TakeScreen(timeout =>
        {
            screenTimeouts.Add(timeout);
            return true;
        });

        Assert.Equal([TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15)], services.WaitTimeouts);
        Assert.Equal([TimeSpan.FromSeconds(15)], screenTimeouts);
    }

    [Fact]
    public void The_stop_timeout_can_be_changed()
    {
        var services = BothRunning();

        new LConnectHandover(services, new ListLog(), TimeSpan.FromSeconds(30)).TakeScreen(Screen(services));

        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)], services.WaitTimeouts);
    }

    [Fact]
    public void Logs_each_step_once()
    {
        var services = BothRunning();
        var log = new ListLog();

        new LConnectHandover(services, log).TakeScreen(Screen(services));

        Assert.Equal(
        [
            "info: Stopping L-Connect's service watcher (LConnectServiceWatcher) so it doesn't restart the service while Marquee takes the screen.",
            "info: Stopping L-Connect's service (LConnectService) so Marquee can open the screen.",
            "info: L-Connect's service has stopped; opening the screen.",
            "info: Marquee has the screen.",
            "info: Starting L-Connect's service again. It skips the screen and keeps running your fans and RGB.",
            "info: Starting L-Connect's service watcher again.",
        ], log.Entries);
    }

    [Fact]
    public void A_service_that_stops_too_slowly_fails_the_handover_and_both_start_again()
    {
        var services = BothRunning();
        services.StopLate.Add(Service);
        var log = new ListLog();

        var result = new LConnectHandover(services, log).TakeScreen(Screen(services));

        Assert.Equal(
            [$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"wait {Service} Stopped", $"start {Service}", $"start {Watcher}"],
            services.Calls);
        Assert.Equal(HandoverOutcome.Failed, result.Outcome);
        Assert.Equal("L-Connect's service didn't stop within 15 s.", result.Message);
        Assert.Equal([Service, Watcher], result.Restarted);
        Assert.Contains("warn: L-Connect's service didn't stop within 15 s.", log.Entries);
    }

    [Fact]
    public void A_service_stuck_stopping_still_gets_its_watcher_back_and_the_result_says_L_Connect_did_not_start()
    {
        var services = BothRunning();
        services.NeverStop.Add(Service);
        var log = new ListLog();

        var result = new LConnectHandover(services, log).TakeScreen(Screen(services));

        // The second wait gives a slow stop one more chance before the start, which can't work while it's stopping.
        Assert.Equal(
            [$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"wait {Service} Stopped", $"wait {Service} Stopped", $"start {Service}", $"start {Watcher}"],
            services.Calls);
        Assert.Equal(HandoverOutcome.Failed, result.Outcome);
        Assert.Equal(
            "L-Connect's service didn't stop within 15 s. L-Connect didn't start again (see the log); " +
            "start it from its tray icon's start-service item.",
            result.Message);
        Assert.Equal([Watcher], result.Restarted);
        Assert.Equal([Service], result.RestartFailed);
        Assert.Contains(log.Entries, e => e.StartsWith("error: Couldn't start L-Connect's service again: ", StringComparison.Ordinal));
        Assert.Equal(Running, services.Status(Watcher));
    }

    [Fact]
    public void A_screen_that_does_not_open_fails_the_handover_and_both_start_again()
    {
        var services = BothRunning();
        var log = new ListLog();

        var result = new LConnectHandover(services, log).TakeScreen(Screen(services, opens: false));

        Assert.Equal(
            [$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"wait {Service} Stopped", "screen", $"start {Service}", $"start {Watcher}"],
            services.Calls);
        Assert.Equal(HandoverOutcome.Failed, result.Outcome);
        Assert.Equal("Marquee couldn't open the screen within 15 s of L-Connect's service stopping.", result.Message);
        Assert.Equal([Service, Watcher], result.Restarted);
        Assert.Contains("warn: Marquee couldn't open the screen within 15 s of L-Connect's service stopping.", log.Entries);
    }

    [Fact]
    public void An_error_while_opening_the_screen_still_starts_both_again()
    {
        var services = BothRunning();
        var log = new ListLog();

        var result = new LConnectHandover(services, log).TakeScreen(_ =>
        {
            services.Calls.Add("screen");
            throw new IOException("The device was unplugged.");
        });

        Assert.Equal(
            [$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"wait {Service} Stopped", "screen", $"start {Service}", $"start {Watcher}"],
            services.Calls);
        Assert.Equal(HandoverOutcome.Failed, result.Outcome);
        Assert.Equal("Couldn't take the screen from L-Connect: The device was unplugged.", result.Message);
        Assert.Equal([Service, Watcher], result.Restarted);
        Assert.Contains("error: Couldn't take the screen from L-Connect: The device was unplugged.", log.Entries);
    }

    [Fact]
    public void A_service_that_refuses_to_stop_is_not_started_but_the_watcher_is()
    {
        var services = BothRunning();
        services.FailToStop.Add(Service);

        var result = new LConnectHandover(services, new ListLog()).TakeScreen(Screen(services));

        Assert.Equal([$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"start {Watcher}"], services.Calls);
        Assert.Equal(HandoverOutcome.Failed, result.Outcome);
        Assert.StartsWith("Couldn't take the screen from L-Connect: Cannot open LConnectService service", result.Message);
        Assert.Equal([Watcher], result.Restarted);
        Assert.Empty(result.RestartFailed);
        Assert.Equal(Running, services.Status(Service));
        Assert.Equal(Running, services.Status(Watcher));
    }

    [Fact]
    public void A_watcher_that_refuses_to_stop_leaves_everything_as_it_was()
    {
        var services = BothRunning();
        services.FailToStop.Add(Watcher);

        var result = new LConnectHandover(services, new ListLog()).TakeScreen(Screen(services));

        Assert.Equal([$"stop {Watcher}"], services.Calls);
        Assert.Equal(HandoverOutcome.Failed, result.Outcome);
        Assert.Empty(result.Restarted);
        Assert.Equal(Running, services.Status(Service));
        Assert.Equal(Running, services.Status(Watcher));
    }

    [Fact]
    public void A_watcher_that_stops_too_slowly_fails_the_handover_before_the_service_is_touched()
    {
        // Still stopping, the watcher may restart the service at its last look, which would take the screen back.
        var services = BothRunning();
        services.StopLate.Add(Watcher);
        var log = new ListLog();

        var result = new LConnectHandover(services, log).TakeScreen(Screen(services));

        Assert.Equal([$"stop {Watcher}", $"wait {Watcher} Stopped", $"start {Watcher}"], services.Calls);
        Assert.Equal(HandoverOutcome.Failed, result.Outcome);
        Assert.Equal("L-Connect's service watcher didn't stop within 15 s.", result.Message);
        Assert.Equal([Watcher], result.Restarted);
        Assert.Contains("warn: L-Connect's service watcher didn't stop within 15 s.", log.Entries);
        Assert.Equal(Running, services.Status(Service));
        Assert.Equal(Running, services.Status(Watcher));
    }

    [Fact]
    public void A_wait_for_the_screen_that_is_cancelled_is_its_own_outcome_and_starts_both_again()
    {
        var services = BothRunning();
        var log = new ListLog();
        using var cancel = new CancellationTokenSource();

        var result = new LConnectHandover(services, log).TakeScreen(_ =>
        {
            // Marquee pauses or quits while it waits for the screen.
            services.Calls.Add("screen");
            cancel.Cancel();
            return false;
        }, cancel.Token);

        Assert.Equal(
            [$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"wait {Service} Stopped", "screen", $"start {Service}", $"start {Watcher}"],
            services.Calls);
        Assert.Equal(HandoverOutcome.Cancelled, result.Outcome);
        Assert.Equal("Stopped waiting for the screen (Marquee paused or is quitting); starting L-Connect again.", result.Message);
        Assert.Equal([Service, Watcher], result.Restarted);
        Assert.Contains("info: Stopped waiting for the screen (Marquee paused or is quitting); starting L-Connect again.", log.Entries);
        Assert.DoesNotContain(log.Entries, e => e.StartsWith("warn:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_handover_cancelled_before_it_stops_anything_leaves_L_Connect_alone()
    {
        var services = BothRunning();
        var log = new ListLog();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var result = new LConnectHandover(services, log).TakeScreen(Screen(services), cancel.Token);

        Assert.Empty(services.Calls);
        Assert.Equal(HandoverOutcome.Cancelled, result.Outcome);
        Assert.Equal("Marquee paused or is quitting, so L-Connect's service was left running.", result.Message);
        Assert.Empty(result.Restarted);
        Assert.Equal(["info: Marquee paused or is quitting, so L-Connect's service was left running."], log.Entries);
    }

    [Fact]
    public void A_handover_cancelled_while_the_watcher_stops_leaves_the_service_running_and_starts_the_watcher_again()
    {
        var services = BothRunning();
        var log = new ListLog();
        using var cancel = new CancellationTokenSource();
        services.DuringWait = name =>
        {
            if (name == Watcher) cancel.Cancel(); // Marquee pauses or quits while the watcher stops
        };

        var result = new LConnectHandover(services, log).TakeScreen(Screen(services), cancel.Token);

        Assert.Equal([$"stop {Watcher}", $"wait {Watcher} Stopped", $"start {Watcher}"], services.Calls);
        Assert.Equal(HandoverOutcome.Cancelled, result.Outcome);
        Assert.Equal("Marquee paused or is quitting, so L-Connect's service was left running.", result.Message);
        Assert.Equal([Watcher], result.Restarted);
        Assert.Equal(Running, services.Status(Service));
        Assert.Equal(Running, services.Status(Watcher));
        Assert.DoesNotContain(log.Entries, e => e.StartsWith("warn:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_service_that_fails_to_start_does_not_keep_the_watcher_from_starting()
    {
        var services = BothRunning();
        services.FailToStart.Add(Service);

        var result = new LConnectHandover(services, new ListLog()).TakeScreen(Screen(services));

        Assert.Equal(HandoverOutcome.Taken, result.Outcome);
        Assert.Equal(
            "Marquee has the screen, but L-Connect didn't start again (see the log); " +
            "start it from its tray icon's start-service item.",
            result.Message);
        Assert.Equal([Watcher], result.Restarted);
        Assert.Equal([Service], result.RestartFailed);
        Assert.Equal(Running, services.Status(Watcher));
    }

    [Fact]
    public void A_service_someone_else_started_meanwhile_is_left_running()
    {
        // The L-Connect 3 app starts the service when it can't reach it.
        var services = BothRunning();
        var log = new ListLog();

        var result = new LConnectHandover(services, log).TakeScreen(_ =>
        {
            services.SetStatus(Service, Running);
            return true;
        });

        Assert.DoesNotContain($"start {Service}", services.Calls);
        Assert.Contains($"start {Watcher}", services.Calls);
        Assert.Equal(HandoverOutcome.Taken, result.Outcome);
        Assert.Equal([Watcher], result.Restarted);
        Assert.Empty(result.RestartFailed);
        Assert.Contains("info: L-Connect's service is already running again.", log.Entries);
    }

    [Fact]
    public void Without_the_watcher_only_the_service_is_stopped_and_started()
    {
        var services = new FakeServiceControl((Service, Running));

        var result = new LConnectHandover(services, new ListLog()).TakeScreen(Screen(services));

        Assert.Equal([$"stop {Service}", $"wait {Service} Stopped", "screen", $"start {Service}"], services.Calls);
        Assert.Equal(HandoverOutcome.Taken, result.Outcome);
        Assert.Equal([Service], result.Restarted);
    }

    [Fact]
    public void A_stopped_watcher_is_left_stopped()
    {
        var services = new FakeServiceControl((Service, Running), (Watcher, Stopped));

        var result = new LConnectHandover(services, new ListLog()).TakeScreen(Screen(services));

        Assert.Equal([$"stop {Service}", $"wait {Service} Stopped", "screen", $"start {Service}"], services.Calls);
        Assert.Equal(HandoverOutcome.Taken, result.Outcome);
        Assert.Equal([Service], result.Restarted);
        Assert.Equal(Stopped, services.Status(Watcher));
    }

    [Fact]
    public void Nothing_to_do_when_the_service_is_not_installed()
    {
        var services = new FakeServiceControl((Watcher, Running));
        var log = new ListLog();

        var result = new LConnectHandover(services, log).TakeScreen(Screen(services));

        Assert.Empty(services.Calls);
        Assert.Equal(HandoverOutcome.NotNeeded, result.Outcome);
        Assert.Equal("L-Connect's service isn't installed, so there's no screen to take from it.", result.Message);
        Assert.Empty(result.Restarted);
        Assert.Equal(["info: L-Connect's service isn't installed, so there's no screen to take from it."], log.Entries);
    }

    [Theory]
    [InlineData(ServiceControllerStatus.Stopped)]
    [InlineData(ServiceControllerStatus.StopPending)]
    public void Nothing_to_do_when_the_service_is_stopped_or_stopping(ServiceControllerStatus status)
    {
        var services = new FakeServiceControl((Service, status), (Watcher, Running));

        var result = new LConnectHandover(services, new ListLog()).TakeScreen(Screen(services));

        Assert.Empty(services.Calls);
        Assert.Equal(HandoverOutcome.NotNeeded, result.Outcome);
        Assert.Equal("L-Connect's service isn't running, so there's no screen to take from it.", result.Message);
        Assert.Equal(Running, services.Status(Watcher));
    }

    [Fact]
    public void An_SCM_error_while_checking_the_services_fails_without_touching_them()
    {
        var services = new ThrowingStatus();

        var result = new LConnectHandover(services, new ListLog()).TakeScreen(_ => true);

        Assert.Equal(HandoverOutcome.Failed, result.Outcome);
        Assert.Equal("Couldn't take the screen from L-Connect: Access is denied.", result.Message);
        Assert.Empty(result.Restarted);
    }

    [Fact]
    public void The_real_service_control_reads_a_missing_service_as_not_installed() =>
        // Read-only: asks the SCM about a name no one has installed.
        Assert.Null(new WindowsServiceControl().Status("MarqueeTestsNoSuchService"));

    sealed class ThrowingStatus : IServiceControl
    {
        public ServiceControllerStatus? Status(string name) => throw new InvalidOperationException("Access is denied.");
        public void Stop(string name) => throw new InvalidOperationException("Stop shouldn't be called.");
        public void Start(string name) => throw new InvalidOperationException("Start shouldn't be called.");
        public bool WaitFor(string name, ServiceControllerStatus status, TimeSpan timeout) =>
            throw new InvalidOperationException("WaitFor shouldn't be called.");
    }
}
