using System.Collections.Concurrent;
using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

/// The L-Connect handover through the engine, with a screen that is busy while L-Connect's (fake) service runs: the
/// render loop finds it busy, the engine decides from lconnect.handover, and the handover's wait sees the loop open the
/// screen once the service has stopped. No real service is touched.
public sealed class MarqueeEngineHandoverTests : IDisposable
{
    const string Service = FakeServiceControl.Service;
    const string Watcher = FakeServiceControl.Watcher;

    readonly TempDir _dir = new();
    readonly TempTheme _theme = new(TempTheme.Fill("#FF0000"));
    readonly ListLog _log = new();
    readonly FakeServices _services;
    readonly BlockingCollection<ScreenBusyInfo> _busy = [];
    readonly BlockingCollection<HandoverResult> _finished = [];

    public MarqueeEngineHandoverTests()
    {
        _services = new FakeServices(_dir.File("claude-limits.json")) { Elevated = true };
        // The screen is L-Connect's while its service runs.
        _services.Device = _ => new RecordingDevice { Busy = () => _services.LConnectServices.IsServiceRunning };
    }

    public void Dispose()
    {
        _theme.Dispose();
        _dir.Dispose();
    }

    MarqueeEngine Engine(HandoverMode mode)
    {
        var engine = new MarqueeEngine(new EngineSettings { ThemeFolder = _theme.Folder, Handover = mode }, _log, _services);
        engine.ScreenBusy += _busy.Add;
        engine.HandoverFinished += _finished.Add;
        return engine;
    }

    ScreenBusyInfo NextBusy()
    {
        Assert.True(_busy.TryTake(out var info, Wait.Timeout), "The engine never reported the busy screen.");
        return info;
    }

    HandoverResult NextFinished()
    {
        Assert.True(_finished.TryTake(out var result, Wait.Timeout), "The handover never finished.");
        return result;
    }

    [Fact]
    public void Auto_takes_the_screen_at_start_and_starts_L_Connect_again()
    {
        using var engine = Engine(HandoverMode.Auto);

        Assert.True(engine.Start());

        Assert.Equal(new ScreenBusyInfo(LConnectState.Service, HandoverAction.TakeOver, CanTakeOver: true), NextBusy());
        Assert.Equal(HandoverOutcome.Taken, NextFinished().Outcome);
        Assert.True(Wait.Until(() => _services.LastDevice.FramesPushed > 0), "No frame reached the screen.");
        Assert.Equal(
            [$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"wait {Service} Stopped", $"start {Service}", $"start {Watcher}"],
            _services.LConnectServices.Calls);
        Assert.True(_services.LConnectServices.IsServiceRunning);
    }

    [Fact]
    public void ScreenOpened_comes_after_the_busy_report_of_the_episode_it_ends()
    {
        using var engine = Engine(HandoverMode.Auto);
        var events = new BlockingCollection<string>();
        engine.ScreenBusy += _ => events.Add("busy");
        engine.ScreenOpened += () => events.Add("opened");

        Assert.True(engine.Start());

        Assert.True(events.TryTake(out var first, Wait.Timeout), "No event came.");
        Assert.True(events.TryTake(out var second, Wait.Timeout), "Only one event came.");
        Assert.Equal(("busy", "opened"), (first, second));
    }

    [Fact]
    public void TakeOverScreen_refuses_once_the_engine_has_the_screen()
    {
        _services.LConnectServices.SetStatus(Service, System.ServiceProcess.ServiceControllerStatus.Stopped);
        using var engine = Engine(HandoverMode.Off);
        using var opened = new ManualResetEventSlim();
        engine.ScreenOpened += opened.Set;
        Assert.True(engine.Start());
        Assert.True(opened.Wait(Wait.Timeout), "The screen never opened.");

        // L-Connect's service starts again, finds the screen busy and skips it; a late "Take over" changes nothing.
        _services.LConnectServices.SetStatus(Service, System.ServiceProcess.ServiceControllerStatus.Running);
        Assert.False(engine.TakeOverScreen());

        Assert.Empty(_services.LConnectServices.Calls);
        Assert.Contains("info: Marquee has the screen already, so there's nothing to take from L-Connect.", _log.Entries);
    }

    [Fact]
    public void The_handover_has_the_ring_looked_for_at_once_too()
    {
        // L-Connect's service holds the ring as well as the screen, so the ring isn't found until it has stopped;
        // without a nudge, the sender would look again only 5 s after its first look.
        _services.Ring = new FakeRing();
        _services.RingPresent = () => _services.LConnectServices.Calls.Contains($"stop {Service}");
        using var engine = Engine(HandoverMode.Auto);
        Assert.True(engine.Start());
        Assert.Equal(HandoverOutcome.Taken, NextFinished().Outcome);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.True(Wait.Until(() => _services.Ring.FrameCount > 0), "The ring never lit.");
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, RingSender.LookAgainDelay - TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void Ask_prompts_once_and_TakeOverScreen_takes_the_screen()
    {
        using var engine = Engine(HandoverMode.Ask);
        Assert.True(engine.Start());

        Assert.Equal(new ScreenBusyInfo(LConnectState.Service, HandoverAction.Prompt, CanTakeOver: true), NextBusy());
        Assert.Empty(_services.LConnectServices.Calls);

        Assert.True(engine.TakeOverScreen());

        Assert.Equal(HandoverOutcome.Taken, NextFinished().Outcome);
        Assert.True(Wait.Until(() => _services.LastDevice.FramesPushed > 0), "No frame reached the screen.");
        // Getting the screen ended the episode with no second prompt.
        Assert.Empty(_busy);
    }

    [Fact]
    public void Off_reports_the_busy_screen_and_leaves_L_Connect_alone()
    {
        using var engine = Engine(HandoverMode.Off);
        Assert.True(engine.Start());

        Assert.Equal(new ScreenBusyInfo(LConnectState.Service, HandoverAction.None, CanTakeOver: true), NextBusy());
        Assert.False(_finished.TryTake(out _, TimeSpan.FromMilliseconds(300)));
        Assert.Empty(_services.LConnectServices.Calls);
        Assert.Equal(0, _services.LastDevice.FramesPushed);
    }

    [Fact]
    public void Unelevated_the_engine_cannot_take_over_and_says_why()
    {
        _services.Elevated = false;
        using var engine = Engine(HandoverMode.Auto);
        Assert.True(engine.Start());

        Assert.Equal(new ScreenBusyInfo(LConnectState.Service, HandoverAction.None, CanTakeOver: false), NextBusy());
        Assert.False(engine.TakeOverScreen());
        Assert.Empty(_services.LConnectServices.Calls);
        Assert.Contains(_log.Entries, e => e.StartsWith("warn: Marquee can't take the screen from L-Connect: ", StringComparison.Ordinal));
    }

    /// What the tray's Quit, and so the installer's TrayStopper, waits for: stopping the engine mid-handover stops the
    /// handover's wait for the screen and starts L-Connect again at once, not when the wait would have run out.
    [Fact]
    public void Disposing_the_engine_during_a_handover_starts_L_Connect_again_at_once()
    {
        // The screen stays busy after L-Connect's service stops, so the handover waits for it.
        _services.Device = _ => new RecordingDevice { Busy = () => true };
        var engine = Engine(HandoverMode.Auto);
        Assert.True(engine.Start());
        Assert.Equal(HandoverAction.TakeOver, NextBusy().Action);
        Assert.True(Wait.Until(() => _services.LConnectServices.Calls.Contains($"wait {Service} Stopped")), "The handover never stopped L-Connect.");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        engine.Dispose();

        Assert.InRange(clock.Elapsed, TimeSpan.Zero, LConnectHandover.ScreenTimeout / 2);
        Assert.Equal(
            [$"stop {Watcher}", $"wait {Watcher} Stopped", $"stop {Service}", $"wait {Service} Stopped", $"start {Service}", $"start {Watcher}"],
            _services.LConnectServices.Calls);
        Assert.True(_services.LConnectServices.IsServiceRunning);
        Assert.Equal(HandoverOutcome.Cancelled, NextFinished().Outcome);
    }

    [Fact]
    public void TakeOverScreen_does_nothing_before_Start_or_while_paused()
    {
        using var engine = Engine(HandoverMode.Off);
        Assert.False(engine.TakeOverScreen());

        Assert.True(engine.Start());
        NextBusy();
        engine.Pause();
        Assert.False(engine.TakeOverScreen());

        engine.Stop();
        Assert.False(engine.TakeOverScreen());
        Assert.Empty(_services.LConnectServices.Calls);
    }

    [Fact]
    public void A_resumed_engine_reports_a_screen_that_is_still_busy_again()
    {
        using var engine = Engine(HandoverMode.Ask);
        Assert.True(engine.Start());
        NextBusy();

        engine.Pause();
        engine.Resume();

        Assert.Equal(HandoverAction.Prompt, NextBusy().Action);
    }

    [Fact]
    public void The_engine_sets_the_screen_signal_while_it_has_the_screen()
    {
        _services.LConnectServices.SetStatus(Service, System.ServiceProcess.ServiceControllerStatus.Stopped);
        using var engine = Engine(HandoverMode.Off);
        using var claimScreen = ScreenSignal.Open(_services.ScreenSignalName);
        Assert.NotNull(claimScreen);
        Assert.False(claimScreen.IsSet);

        Assert.True(engine.Start());
        Assert.True(claimScreen.Wait(Wait.Timeout), "The engine never said it has the screen.");

        engine.Pause();
        Assert.False(claimScreen.IsSet);
    }
}
