using System.ServiceProcess;
using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

/// The engine's side of the L-Connect handover, on its own: what each mode does with a busy screen, one handover at a
/// time, the back-off after a failure and Auto's retry, and the screen signal. The render loop is played by the test:
/// the screen "opens" when the handover asks for a retry while L-Connect's service is stopped. A fake clock stands in
/// for time, and fake timers for the retry's.
public sealed class EngineHandoverTests : IDisposable
{
    const string Service = FakeServiceControl.Service;
    const string Watcher = FakeServiceControl.Watcher;

    const string Retrying = "info: Trying again to take the screen from L-Connect's service, as `lconnect.handover: auto` says.";

    static readonly string[] FullHandover =
        [$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"wait {Service} Stopped", $"start {Service}", $"start {Watcher}"];

    readonly FakeServices _services = new("unused-claude-limits.json") { Elevated = true };
    readonly ListLog _log = new();
    readonly List<ScreenBusyInfo> _busy = [];
    readonly List<HandoverResult> _finished = [];
    readonly FakeTimers _timers = new();
    readonly EngineHandover _handover;
    TimeSpan _now = TimeSpan.FromMinutes(5);
    volatile bool _screenOpens = true;
    HandoverMode _mode = HandoverMode.Off;

    public EngineHandoverTests()
    {
        _handover = new EngineHandover(_log, _services, mode: () => _mode,
            retryScreen: () =>
            {
                // The render loop: with L-Connect's service stopped, the next frame opens the screen.
                if (_screenOpens && !_services.LConnectServices.IsServiceRunning) _handover!.OnScreenOpenChanged(true);
            },
            busy: info =>
            {
                lock (_busy) _busy.Add(info);
            },
            finished: result =>
            {
                lock (_finished) _finished.Add(result);
            },
            elapsed: () => _now,
            schedule: _timers.Schedule);
        _handover.SetScreenWanted(true);
    }

    public void Dispose()
    {
        _handover.WaitForEnd();
        _handover.Dispose();
    }

    FakeServiceControl LConnect => _services.LConnectServices;

    /// The screen comes up busy with `lconnect.handover` at `mode`, and whatever handover that starts runs to its end.
    void Busy(HandoverMode mode, LConnectState state = LConnectState.Service)
    {
        _mode = mode;
        _handover.OnScreenBusy(state);
        _handover.WaitForEnd();
    }

    /// Auto's retry, the one timer pending, fires once its time has come, and whatever handover it starts runs to its end.
    void FireRetry()
    {
        var retry = Assert.Single(_timers.Pending);
        _now += retry.Delay;
        retry.Fire();
        _handover.WaitForEnd();
    }

    [Fact]
    public void Auto_takes_the_screen_from_L_Connect_and_starts_L_Connect_again()
    {
        Busy(HandoverMode.Auto);

        Assert.Equal([new ScreenBusyInfo(LConnectState.Service, HandoverAction.TakeOver, CanTakeOver: true)], _busy);
        Assert.Equal(FullHandover, LConnect.Calls);
        var result = Assert.Single(_finished);
        Assert.Equal(HandoverOutcome.Taken, result.Outcome);
        Assert.True(LConnect.IsServiceRunning);
        Assert.Equal(ServiceControllerStatus.Running, LConnect.Status(Watcher));
        Assert.Contains("info: L-Connect's service has the screen; taking it over, as `lconnect.handover: auto` says.", _log.Entries);
    }

    [Fact]
    public void Auto_takes_over_again_at_the_next_busy_episode_after_one_that_worked()
    {
        Busy(HandoverMode.Auto);
        _now += TimeSpan.FromSeconds(1);
        _handover.OnScreenOpenChanged(false); // unplugged, say, and L-Connect had it first when it came back
        Busy(HandoverMode.Auto);

        Assert.Equal([.. FullHandover, .. FullHandover], LConnect.Calls);
        Assert.Equal([HandoverOutcome.Taken, HandoverOutcome.Taken], _finished.Select(r => r.Outcome));
    }

    [Fact]
    public void Auto_leaves_the_screen_alone_for_60_seconds_after_a_handover_that_failed()
    {
        LConnect.FailToStop = true;
        Busy(HandoverMode.Auto);
        Assert.Equal(HandoverOutcome.Failed, Assert.Single(_finished).Outcome);
        var tried = LConnect.Calls.Count;

        _now += TimeSpan.FromSeconds(59);
        Busy(HandoverMode.Auto);
        Assert.Equal(tried, LConnect.Calls.Count);
        Assert.Single(_finished);
        Assert.Contains(
            "info: L-Connect's service has the screen again, but taking it failed less than 60 s ago, so Marquee waits until then to try again.",
            _log.Entries);
        // The tray still hears of the busy screen, and can still offer to take it.
        Assert.Equal(2, _busy.Count);

        _now += TimeSpan.FromSeconds(2);
        LConnect.FailToStop = false;
        Busy(HandoverMode.Auto);
        Assert.Equal(HandoverOutcome.Taken, _finished[^1].Outcome);
    }

    [Fact]
    public void A_failed_take_over_from_the_tray_holds_Auto_back_too()
    {
        LConnect.FailToStop = true;
        Assert.True(_handover.TakeOver());
        _handover.WaitForEnd();
        var tried = LConnect.Calls.Count;

        Busy(HandoverMode.Auto);

        Assert.Equal(tried, LConnect.Calls.Count);
    }

    [Fact]
    public void Ask_prompts_and_leaves_L_Connect_alone()
    {
        Busy(HandoverMode.Ask);

        Assert.Equal([new ScreenBusyInfo(LConnectState.Service, HandoverAction.Prompt, CanTakeOver: true)], _busy);
        Assert.Empty(LConnect.Calls);
        Assert.Empty(_finished);
    }

    [Fact]
    public void Off_reports_the_busy_screen_and_does_nothing_else()
    {
        Busy(HandoverMode.Off);

        Assert.Equal([new ScreenBusyInfo(LConnectState.Service, HandoverAction.None, CanTakeOver: true)], _busy);
        Assert.Empty(LConnect.Calls);
        Assert.Empty(_log.Entries);
    }

    [Theory]
    [InlineData(LConnectState.AppOnly)]
    [InlineData(LConnectState.NotRunning)]
    public void Only_L_Connect_s_service_can_be_taken_over_from(LConnectState state)
    {
        Busy(HandoverMode.Auto, state);

        Assert.Equal([new ScreenBusyInfo(state, HandoverAction.None, CanTakeOver: false)], _busy);
        Assert.Empty(LConnect.Calls);
    }

    [Theory]
    [InlineData(HandoverMode.Ask)]
    [InlineData(HandoverMode.Auto)]
    public void Unelevated_the_handover_is_unavailable_and_the_log_says_why_once(HandoverMode mode)
    {
        _services.Elevated = false;

        Busy(mode);
        _handover.OnScreenOpenChanged(true);
        _handover.OnScreenOpenChanged(false);
        Busy(mode);

        Assert.Equal(2, _busy.Count);
        Assert.All(_busy, info => Assert.Equal(new ScreenBusyInfo(LConnectState.Service, HandoverAction.None, CanTakeOver: false), info));
        Assert.Empty(LConnect.Calls);
        Assert.Single(_log.Entries, e => e == "warn: " + HandoverNeedsAdministrator);
    }

    const string HandoverNeedsAdministrator =
        "Marquee can't take the screen from L-Connect: stopping L-Connect's service needs Marquee to run as " +
        "administrator. Set `lconnect: { handover: off }` in config.yaml to stop this message.";

    [Fact]
    public void Unelevated_with_the_handover_off_says_nothing()
    {
        _services.Elevated = false;

        Busy(HandoverMode.Off);

        Assert.Equal([new ScreenBusyInfo(LConnectState.Service, HandoverAction.None, CanTakeOver: false)], _busy);
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public void Unelevated_TakeOver_refuses_and_says_why()
    {
        _services.Elevated = false;

        Assert.False(_handover.TakeOver());

        Assert.Empty(LConnect.Calls);
        Assert.Equal(["warn: " + TakeOverNeedsAdministrator], _log.Entries);
    }

    // Not HandoverNeedsAdministrator: someone clicked "Take over", so pointing them at `handover: off` makes no sense.
    const string TakeOverNeedsAdministrator =
        "Marquee can't take the screen from L-Connect: stopping L-Connect's service needs Marquee to run as " +
        "administrator, as its logon task starts it.";

    [Fact]
    public void TakeOver_runs_one_handover_at_a_time_and_reports_how_it_ended()
    {
        _screenOpens = false; // the screen opens when the test says so
        Assert.True(_handover.TakeOver());
        Assert.True(Wait.Until(() => LConnect.Calls.Contains($"wait {Service} Stopped")), "The handover never stopped L-Connect.");
        Assert.True(_handover.IsRunning);

        Assert.False(_handover.TakeOver());
        _mode = HandoverMode.Auto;
        _handover.OnScreenBusy(LConnectState.Service);
        _handover.OnScreenOpenChanged(true);
        _handover.WaitForEnd();

        Assert.False(_handover.IsRunning);
        Assert.Equal(FullHandover, LConnect.Calls);
        var result = Assert.Single(_finished);
        Assert.Equal(HandoverOutcome.Taken, result.Outcome);
        Assert.Equal("Marquee has the screen, and L-Connect is running your fans and RGB again.", result.Message);

        // Done, so another may start, once the screen is L-Connect's again.
        _handover.OnScreenOpenChanged(false);
        _screenOpens = true;
        Assert.True(_handover.TakeOver());
    }

    [Fact]
    public void The_handover_waits_for_the_screen_to_open_and_asks_the_loop_to_try_it_at_once()
    {
        var retries = 0;
        using var handover = new EngineHandover(_log, _services, mode: () => HandoverMode.Off, retryScreen: () => Interlocked.Increment(ref retries),
            busy: _ => { }, finished: _ => { });
        handover.SetScreenWanted(true);

        Assert.True(handover.TakeOver());
        Assert.True(Wait.Until(() => Volatile.Read(ref retries) == 1), "The handover never asked for the screen.");
        Assert.True(handover.IsRunning);

        handover.OnScreenOpenChanged(true);
        handover.WaitForEnd();
        Assert.Equal(FullHandover, LConnect.Calls);
    }

    /// A stale "Take over" (a balloon clicked long after it showed, a menu built just before the screen opened), or a
    /// busy report that comes after the screen opened, must not stop L-Connect for nothing.
    [Fact]
    public void Nothing_is_taken_over_once_Marquee_has_the_screen()
    {
        _handover.OnScreenOpenChanged(true);

        Assert.False(_handover.TakeOver());
        Busy(HandoverMode.Auto);

        Assert.Empty(LConnect.Calls);
        Assert.Empty(_finished);
        Assert.Contains("info: Marquee has the screen already, so there's nothing to take from L-Connect.", _log.Entries);
    }

    [Fact]
    public void After_a_failed_handover_Auto_tries_again_60_seconds_later_while_the_screen_stays_busy()
    {
        LConnect.FailToStop = true;
        Busy(HandoverMode.Auto);
        Assert.Equal(HandoverOutcome.Failed, Assert.Single(_finished).Outcome);
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(_timers.Pending).Delay);

        LConnect.FailToStop = false;
        FireRetry();

        Assert.Equal([HandoverOutcome.Failed, HandoverOutcome.Taken], _finished.Select(r => r.Outcome));
        Assert.Equal([$"stop {Watcher}", .. FullHandover], LConnect.Calls);
        Assert.Contains(Retrying, _log.Entries);
        Assert.DoesNotContain(_log.Entries, e => e.Contains("less than 60 s ago", StringComparison.Ordinal));
        Assert.Empty(_timers.Pending);
    }

    [Fact]
    public void Auto_keeps_trying_every_60_seconds_while_taking_the_screen_fails()
    {
        LConnect.FailToStop = true;
        Busy(HandoverMode.Auto);

        FireRetry();
        FireRetry();

        Assert.Equal(3, _finished.Count);
        Assert.All(_finished, result => Assert.Equal(HandoverOutcome.Failed, result.Outcome));
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(_timers.Pending).Delay);
    }

    [Fact]
    public void The_retry_does_nothing_once_the_mode_is_no_longer_Auto()
    {
        LConnect.FailToStop = true;
        Busy(HandoverMode.Auto);
        var tried = LConnect.Calls.Count;

        _mode = HandoverMode.Ask; // config.yaml changed meanwhile
        FireRetry();

        Assert.Equal(tried, LConnect.Calls.Count);
        Assert.Single(_finished);
    }

    [Fact]
    public void The_retry_does_nothing_once_Marquee_has_the_screen()
    {
        LConnect.FailToStop = true;
        Busy(HandoverMode.Auto);
        var tried = LConnect.Calls.Count;

        _handover.OnScreenOpenChanged(true); // L-Connect let go by itself
        FireRetry();

        Assert.Equal(tried, LConnect.Calls.Count);
        Assert.DoesNotContain(Retrying, _log.Entries);
    }

    [Fact]
    public void The_retry_does_nothing_once_L_Connect_s_service_has_stopped()
    {
        LConnect.FailToStop = true;
        Busy(HandoverMode.Auto);
        var tried = LConnect.Calls.Count;

        LConnect.SetStatus(Service, ServiceControllerStatus.Stopped);
        FireRetry();

        Assert.Equal(tried, LConnect.Calls.Count);
        Assert.DoesNotContain(Retrying, _log.Entries);
    }

    [Fact]
    public void Pausing_or_disposing_cancels_the_retry()
    {
        LConnect.FailToStop = true;
        Busy(HandoverMode.Auto);
        var retry = Assert.Single(_timers.Pending);

        _handover.SetScreenWanted(false);

        Assert.True(retry.IsCancelled);
        _handover.SetScreenWanted(true);
        _now += TimeSpan.FromSeconds(1);
        Busy(HandoverMode.Auto); // held back, so it waits for the retry again
        var again = Assert.Single(_timers.Pending);

        _handover.Dispose();

        Assert.True(again.IsCancelled);
    }

    [Fact]
    public void A_busy_screen_held_back_after_a_failure_is_tried_again_once_the_60_seconds_are_up()
    {
        // Paused just after a failure, then resumed: the retry went with the pause, and the new busy episode is held
        // back, so it brings its own retry for the rest of the wait.
        LConnect.FailToStop = true;
        Busy(HandoverMode.Auto);
        _handover.SetScreenWanted(false);
        _handover.SetScreenWanted(true);

        _now += TimeSpan.FromSeconds(20);
        Busy(HandoverMode.Auto);
        Assert.Equal(TimeSpan.FromSeconds(40), Assert.Single(_timers.Pending).Delay);

        LConnect.FailToStop = false;
        FireRetry();

        Assert.Equal(HandoverOutcome.Taken, _finished[^1].Outcome);
    }

    [Fact]
    public void A_retry_already_running_when_it_was_cancelled_leaves_the_retry_that_replaced_it_alone()
    {
        // The first retry's callback has started and waits for the lock while a pause cancels it, and a resume with the
        // screen still busy is held back and schedules a retry of its own for the rest of the 60 seconds.
        LConnect.FailToStop = true;
        Busy(HandoverMode.Auto);
        var first = Assert.Single(_timers.Pending);
        _handover.SetScreenWanted(false);
        _handover.SetScreenWanted(true);
        _now += TimeSpan.FromSeconds(20);
        Busy(HandoverMode.Auto);
        var second = Assert.Single(_timers.Pending);
        var tried = LConnect.Calls.Count;

        first.FireAfterCancel();
        _handover.WaitForEnd();

        Assert.Equal(tried, LConnect.Calls.Count);
        Assert.Single(_finished);
        Assert.True(second.IsPending);

        LConnect.FailToStop = false;
        FireRetry();
        Assert.Equal(HandoverOutcome.Taken, _finished[^1].Outcome);
    }

    [Fact]
    public void A_retry_already_running_when_a_pause_cancelled_it_does_nothing_after_the_resume()
    {
        LConnect.FailToStop = true;
        Busy(HandoverMode.Auto);
        var first = Assert.Single(_timers.Pending);
        _handover.SetScreenWanted(false);
        _handover.SetScreenWanted(true); // resumed; the screen hasn't been reported busy again yet
        var tried = LConnect.Calls.Count;

        first.FireAfterCancel();
        _handover.WaitForEnd();

        Assert.Equal(tried, LConnect.Calls.Count);
        Assert.DoesNotContain(Retrying, _log.Entries);
    }

    [Fact]
    public void A_take_over_from_the_tray_that_fails_with_the_mode_at_Ask_is_not_retried()
    {
        LConnect.FailToStop = true;
        _mode = HandoverMode.Ask;
        Assert.True(_handover.TakeOver());
        _handover.WaitForEnd();
        var tried = LConnect.Calls.Count;

        foreach (var retry in _timers.Pending) retry.Fire();
        _handover.WaitForEnd();

        Assert.Equal(tried, LConnect.Calls.Count);
    }

    [Fact]
    public void A_handover_cut_short_by_a_pause_does_not_hold_Auto_back()
    {
        _screenOpens = false;
        Assert.True(_handover.TakeOver());
        Assert.True(Wait.Until(() => LConnect.Calls.Contains($"wait {Service} Stopped")), "The handover never stopped L-Connect.");
        _handover.SetScreenWanted(false);
        _handover.WaitForEnd();
        Assert.Equal(HandoverOutcome.Cancelled, Assert.Single(_finished).Outcome);
        Assert.Contains("info: Stopped waiting for the screen (Marquee paused or is quitting); starting L-Connect again.", _log.Entries);

        // Resumed at once, with L-Connect holding the screen again.
        _handover.SetScreenWanted(true);
        _screenOpens = true;
        Busy(HandoverMode.Auto);

        Assert.Equal([HandoverOutcome.Cancelled, HandoverOutcome.Taken], _finished.Select(r => r.Outcome));
        Assert.Empty(_timers.All);
    }

    [Fact]
    public void Once_the_screen_is_no_longer_wanted_a_handover_under_way_starts_L_Connect_again_at_once()
    {
        _screenOpens = false;
        Assert.True(_handover.TakeOver());
        Assert.True(Wait.Until(() => LConnect.Calls.Contains($"wait {Service} Stopped")), "The handover never stopped L-Connect.");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        _handover.SetScreenWanted(false);
        _handover.WaitForEnd();

        // Well within the 15 s the handover would otherwise wait for the screen.
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        Assert.Equal(FullHandover, LConnect.Calls);
        Assert.Equal(HandoverOutcome.Cancelled, Assert.Single(_finished).Outcome);
        Assert.True(LConnect.IsServiceRunning);
    }

    [Fact]
    public void Nothing_starts_while_the_screen_is_not_wanted()
    {
        _handover.SetScreenWanted(false);

        Assert.False(_handover.TakeOver());
        Busy(HandoverMode.Auto);

        Assert.Empty(_busy);
        Assert.Empty(LConnect.Calls);
    }

    [Fact]
    public void The_screen_signal_follows_the_screen()
    {
        using var claimScreen = ScreenSignal.Open(_services.ScreenSignalName);
        Assert.NotNull(claimScreen);
        Assert.False(claimScreen.IsSet);

        _handover.OnScreenOpenChanged(true);
        Assert.True(claimScreen.IsSet);

        _handover.OnScreenOpenChanged(false);
        Assert.False(claimScreen.IsSet);
    }
}
