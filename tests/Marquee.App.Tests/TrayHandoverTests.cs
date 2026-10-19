using Marquee.App.Tests.TestSupport;
using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Host;
using SkiaSharp;

namespace Marquee.App.Tests;

/// The tray's side of the L-Connect handover: Ask's balloon, the take-over and "Always take over at start" items, and
/// what a handover's end shows.
public sealed class TrayHandoverTests : IDisposable
{
    const string PromptText = "Marquee can take it over while L-Connect keeps running your fans and RGB. Click here to take over.";

    /// The one-time offer to a user who never set `lconnect.handover` also says how to have it every time.
    const string FirstRunPromptText = PromptText + " To do this at every start, choose \"Always take over at start\" in Marquee's menu.";

    readonly TrayRig _rig = new();

    public void Dispose() => _rig.Dispose();

    FakeTrayEngine Engine => _rig.Engine;

    FakeHost Host => _rig.Host;

    static ScreenBusyInfo Busy(HandoverAction action = HandoverAction.Prompt, bool canTakeOver = true,
        LConnectState state = LConnectState.Service) => new(state, action, canTakeOver);

    int TakeOvers => Engine.CallNames.Count(call => call == "TakeOverScreen");

    [Fact]
    public async Task Ask_shows_one_balloon_whose_click_takes_over_the_screen_on_the_worker()
    {
        Engine.RaiseScreenBusy(Busy());

        Assert.Equal([("L-Connect is using the screen", PromptText, false)], Host.Balloons);

        Host.ClickBalloon();
        Host.ClickBalloon(); // the first click closed it
        await _rig.Flush();

        var call = Assert.Single(Engine.Calls);
        Assert.Equal("TakeOverScreen", call.Call);
        Assert.NotEqual(Environment.CurrentManagedThreadId, call.Thread);
    }

    [Fact]
    public async Task A_click_on_a_later_different_balloon_doesnt_take_over()
    {
        Engine.RaiseScreenBusy(Busy());
        Engine.RaiseThemeFailed("layout.yaml line 4: unknown blend 'sideways'");

        Host.ClickBalloon();
        await _rig.Flush();

        Assert.Equal(2, Host.Balloons.Count);
        Assert.Equal(0, TakeOvers);
    }

    [Fact]
    public void A_busy_screen_with_the_handover_off_shows_no_balloon_but_offers_the_menu_item()
    {
        Engine.RaiseScreenBusy(Busy(HandoverAction.None));

        Assert.Empty(Host.Balloons);
        Assert.Equal("L-Connect is using the screen", _rig.Model.BuildMenu()[0].Text);
        var item = _rig.Item(MenuKeys.TakeOver);
        Assert.Equal("Take over the screen from L-Connect", item?.Text);
        Assert.True(item!.Enabled);
    }

    [Fact]
    public async Task The_take_over_item_takes_over_on_the_worker()
    {
        Engine.RaiseScreenBusy(Busy(HandoverAction.None));

        _rig.Item(MenuKeys.TakeOver)!.Command!();
        await _rig.Flush();

        Assert.Equal(1, TakeOvers);
    }

    [Fact]
    public void The_take_over_item_goes_once_the_screen_opens_or_the_handover_took_it()
    {
        Engine.RaiseScreenBusy(Busy(HandoverAction.None));
        Assert.NotNull(_rig.Item(MenuKeys.TakeOver));

        // The engine raises ScreenOpened after the ScreenBusy it ends, through the same queue; a frame, raised on the
        // screen's own thread, could come before a late ScreenBusy and leave a stale "Take over".
        using (var frame = SKImage.Create(new SKImageInfo(4, 4)))
            Engine.RaiseFrameRendered(frame);
        Assert.NotNull(_rig.Item(MenuKeys.TakeOver));
        Engine.RaiseScreenOpened();

        Assert.Null(_rig.Item(MenuKeys.TakeOver));
        Assert.Equal("example-landscape · 30 fps · Universal Screen 8.8", _rig.Model.BuildMenu()[0].Text);

        Engine.RaiseScreenBusy(Busy(HandoverAction.None));
        Engine.RaiseHandoverFinished(new HandoverResult(HandoverOutcome.Taken, "Marquee has the screen.", ["LConnectService"], []));

        Assert.Null(_rig.Item(MenuKeys.TakeOver));
        Assert.Empty(Host.Balloons); // the picture says it worked
    }

    [Fact]
    public void Paused_there_is_nothing_to_take_over()
    {
        Engine.RaiseScreenBusy(Busy(HandoverAction.None));
        Engine.IsPaused = true;

        Assert.Null(_rig.Item(MenuKeys.TakeOver));
        Assert.Equal("Paused: the screen is free for other programs", _rig.Model.BuildMenu()[0].Text);
    }

    [Theory]
    [InlineData(LConnectState.AppOnly)]
    [InlineData(LConnectState.NotRunning)]
    public void A_screen_busy_for_another_reason_has_no_take_over(LConnectState state)
    {
        Engine.RaiseScreenBusy(Busy(HandoverAction.None, canTakeOver: false, state));

        Assert.Empty(Host.Balloons);
        Assert.Null(_rig.Item(MenuKeys.TakeOver));
        Assert.DoesNotContain(_rig.Model.BuildMenu(), item => item.Text.Contains("L-Connect", StringComparison.Ordinal));
    }

    [Fact]
    public void Unelevated_there_is_no_balloon_or_item_but_a_line_saying_why()
    {
        Engine.IsElevated = false;

        // The engine turns Ask's prompt into None when it can't take over, and says CanTakeOver false.
        Engine.RaiseScreenBusy(Busy(HandoverAction.None, canTakeOver: false));

        var menu = _rig.Model.BuildMenu();
        Assert.Empty(Host.Balloons);
        Assert.DoesNotContain(menu, item => item.Key is MenuKeys.TakeOver or MenuKeys.AlwaysTakeOver);
        Assert.Equal("L-Connect is using the screen", menu[0].Text);
        var why = menu[1];
        Assert.Equal(MenuKeys.Problem, why.Key);
        Assert.Equal("Marquee can take the screen from L-Connect only when it runs as administrator, as its logon task starts it.", why.Text);
        Assert.False(why.Enabled);
    }

    [Fact]
    public void Always_take_over_at_start_is_checked_when_the_handover_is_auto()
    {
        Engine.Settings = Engine.Settings! with { Handover = HandoverMode.Ask };
        var item = _rig.Item(MenuKeys.AlwaysTakeOver);
        Assert.Equal("Always take over at start", item?.Text);
        Assert.False(item!.Checked);

        Engine.Settings = Engine.Settings with { Handover = HandoverMode.Auto };
        Assert.True(_rig.Item(MenuKeys.AlwaysTakeOver)!.Checked);
    }

    [Fact]
    public async Task Checking_always_take_over_writes_auto_and_unchecking_writes_ask()
    {
        File.WriteAllText(_rig.Paths.ConfigFile, "# mine\ntheme: example-landscape\n");
        Engine.Settings = Engine.Settings! with { Handover = HandoverMode.Off };

        _rig.Item(MenuKeys.AlwaysTakeOver)!.Command!();
        await _rig.Flush();

        var config = File.ReadAllText(_rig.Paths.ConfigFile);
        Assert.StartsWith("# mine\ntheme: example-landscape\n", config);
        Assert.Equal(HandoverMode.Auto, ConfigFile.Read(_rig.Paths.ConfigFile).Handover);

        Engine.Settings = Engine.Settings with { Handover = HandoverMode.Auto }; // the config watcher applied it
        _rig.Item(MenuKeys.AlwaysTakeOver)!.Command!();
        await _rig.Flush();

        Assert.Equal(HandoverMode.Ask, ConfigFile.Read(_rig.Paths.ConfigFile).Handover);
        Assert.Empty(Engine.Calls); // the watcher applies it, not the menu
    }

    /// The engine reported this busy episode before the mode was auto, so Auto alone would wait for the next one.
    [Fact]
    public async Task Checking_always_take_over_while_L_Connect_has_the_screen_takes_it_over_now()
    {
        File.WriteAllText(_rig.Paths.ConfigFile, "theme: example-landscape\n");
        Engine.Settings = Engine.Settings! with { Handover = HandoverMode.Off };
        Engine.RaiseScreenBusy(Busy(HandoverAction.None));

        _rig.Item(MenuKeys.AlwaysTakeOver)!.Command!();
        await _rig.Flush();

        Assert.Equal(HandoverMode.Auto, ConfigFile.Read(_rig.Paths.ConfigFile).Handover);
        var call = Assert.Single(Engine.Calls);
        Assert.Equal("TakeOverScreen", call.Call);
        Assert.NotEqual(Environment.CurrentManagedThreadId, call.Thread);
    }

    [Fact]
    public async Task Unchecking_always_take_over_or_checking_it_with_nothing_to_take_doesnt_take_over()
    {
        File.WriteAllText(_rig.Paths.ConfigFile, "theme: example-landscape\n");
        Engine.Settings = Engine.Settings! with { Handover = HandoverMode.Auto };
        Engine.RaiseScreenBusy(Busy(HandoverAction.None)); // held back after a failure, say
        _rig.Item(MenuKeys.AlwaysTakeOver)!.Command!();
        await _rig.Flush();
        Assert.Equal(HandoverMode.Ask, ConfigFile.Read(_rig.Paths.ConfigFile).Handover);

        Engine.Settings = Engine.Settings with { Handover = HandoverMode.Ask };
        Engine.RaiseScreenOpened();
        _rig.Item(MenuKeys.AlwaysTakeOver)!.Command!();
        await _rig.Flush();
        Assert.Equal(HandoverMode.Auto, ConfigFile.Read(_rig.Paths.ConfigFile).Handover);

        Assert.Equal(0, TakeOvers);
    }

    [Fact]
    public async Task A_config_yaml_marquee_cant_change_says_so_and_stays_as_it_is()
    {
        File.WriteAllText(_rig.Paths.ConfigFile, "lconnect: off\n");

        await _rig.Model.SetAlwaysTakeOver(true).WaitAsync(Wait.Timeout);

        Assert.Equal("lconnect: off\n", File.ReadAllText(_rig.Paths.ConfigFile));
        Assert.True(Assert.Single(Host.Balloons).IsError);
    }

    [Fact]
    public void A_failed_handover_shows_an_error_balloon_at_most_once_a_minute()
    {
        var failed = new HandoverResult(HandoverOutcome.Failed, "L-Connect's service didn't stop within 15 seconds.", [], []);

        Engine.RaiseHandoverFinished(failed);
        Engine.RaiseHandoverFinished(failed);

        Assert.Equal(
            [("Couldn't take the screen from L-Connect", "L-Connect's service didn't stop within 15 seconds. See the log for details.", true)],
            Host.Balloons);
    }

    [Fact]
    public void A_handover_with_nothing_to_take_from_is_quiet()
    {
        Engine.RaiseHandoverFinished(new HandoverResult(HandoverOutcome.NotNeeded, "L-Connect's service isn't running.", [], []));

        Assert.Empty(Host.Balloons);
    }

    [Fact]
    public void A_handover_cut_short_by_pausing_or_quitting_is_quiet()
    {
        Engine.RaiseHandoverFinished(new HandoverResult(HandoverOutcome.Cancelled,
            "Stopped waiting for the screen (Marquee paused or is quitting); starting L-Connect again.", ["LConnectService"], []));

        Assert.Empty(Host.Balloons);
        Assert.DoesNotContain(_rig.Model.BuildMenu(), item => item.Key == MenuKeys.Problem);
    }

    [Fact]
    public void With_auto_a_failed_handover_says_Marquee_keeps_trying()
    {
        Engine.Settings = Engine.Settings! with { Handover = HandoverMode.Auto };

        Engine.RaiseHandoverFinished(new HandoverResult(HandoverOutcome.Failed, "L-Connect's service didn't stop within 15 s.", [], []));

        Assert.Equal(
            [("Couldn't take the screen from L-Connect", "L-Connect's service didn't stop within 15 s. Marquee will keep trying. See the log for details.", true)],
            Host.Balloons);
    }

    [Fact]
    public void A_failed_handover_is_a_menu_line_until_the_screen_is_Marquee_s()
    {
        Engine.RaiseScreenBusy(Busy(HandoverAction.None));
        Engine.RaiseHandoverFinished(new HandoverResult(HandoverOutcome.Failed, "L-Connect's service didn't stop within 15 s.", [], []));

        var line = _rig.Model.BuildMenu()[1];
        Assert.Equal((MenuKeys.Problem, "Couldn't take the screen from L-Connect: L-Connect's service didn't stop within 15 s.", false),
            (line.Key, line.Text, line.Enabled));

        Engine.RaiseHandoverFinished(new HandoverResult(HandoverOutcome.Taken, "Marquee has the screen.", ["LConnectService"], []));
        Assert.DoesNotContain(_rig.Model.BuildMenu(), item => item.Key == MenuKeys.Problem);

        Engine.RaiseHandoverFinished(new HandoverResult(HandoverOutcome.Failed, "L-Connect's service didn't stop within 15 s.", [], []));
        Engine.RaiseScreenOpened(); // L-Connect let go by itself
        Assert.DoesNotContain(_rig.Model.BuildMenu(), item => item.Key == MenuKeys.Problem);
    }

    [Fact]
    public void A_theme_error_doesnt_hold_back_a_failed_handover_s_balloon()
    {
        Engine.RaiseThemeFailed("layout.yaml line 4: unknown blend 'sideways'");
        Engine.RaiseHandoverFinished(new HandoverResult(HandoverOutcome.Failed, "L-Connect's service didn't stop within 15 s.", [], []));

        Assert.Equal(["The theme didn't load", "Couldn't take the screen from L-Connect"], Host.Balloons.Select(b => b.Title));
    }

    [Fact]
    public async Task A_user_who_never_chose_is_offered_the_handover_once_even_with_it_off()
    {
        Engine.HandoverChosen = false; // no lconnect.handover in config.yaml: off by default, not by choice

        Engine.RaiseScreenBusy(Busy(HandoverAction.None));

        Assert.Equal([("L-Connect is using the screen", FirstRunPromptText, false)], Host.Balloons);
        Host.ClickBalloon();
        await _rig.Flush();
        Assert.Equal(1, TakeOvers);

        // Once per user: the next busy screen, in this run or any later one, only has the menu item.
        Assert.True(File.Exists(Path.Combine(_rig.Paths.DataFolder, FirstRun.HandoverOfferMarker)));
        Engine.RaiseScreenBusy(Busy(HandoverAction.None));
        Assert.Single(Host.Balloons);
        Assert.NotNull(_rig.Item(MenuKeys.TakeOver));
    }

    [Fact]
    public void A_user_who_chose_off_is_not_offered_the_handover()
    {
        Engine.RaiseScreenBusy(Busy(HandoverAction.None)); // HandoverChosen: `lconnect: { handover: off }`

        Assert.Empty(Host.Balloons);
        Assert.False(File.Exists(Path.Combine(_rig.Paths.DataFolder, FirstRun.HandoverOfferMarker)));
    }

    [Fact]
    public void The_offer_waits_for_a_busy_screen_it_can_take()
    {
        Engine.HandoverChosen = false;

        Engine.RaiseScreenBusy(Busy(HandoverAction.None, canTakeOver: false)); // unelevated, say

        Assert.Empty(Host.Balloons);
        Assert.False(File.Exists(Path.Combine(_rig.Paths.DataFolder, FirstRun.HandoverOfferMarker)));
    }

    [Fact]
    public async Task The_first_run_theme_hint_waits_until_Marquee_has_the_screen()
    {
        // In the engine's order: Start returns before the first frame finds the screen busy, and ScreenOpened comes only
        // once Marquee's picture is on it. The handover's offer matters more than which theme is shown, and the hint
        // makes sense only once the theme is on the screen.
        Engine.UsesDefaultTheme = true;
        Engine.HandoverChosen = false;

        await _rig.Model.Start().WaitAsync(Wait.Timeout);
        Engine.RaiseScreenBusy(Busy(HandoverAction.None));

        Assert.Equal([("L-Connect is using the screen", FirstRunPromptText, false)], Host.Balloons);
        Assert.False(File.Exists(Path.Combine(_rig.Paths.DataFolder, FirstRun.HintMarker)));

        Engine.RaiseScreenOpened();

        Assert.Equal(["L-Connect is using the screen", "Marquee is showing an example theme"], Host.Balloons.Select(b => b.Title));
        Assert.True(File.Exists(Path.Combine(_rig.Paths.DataFolder, FirstRun.HintMarker)));

        // The hint's click does nothing; the offer's went with its balloon.
        Host.ClickBalloon();
        await _rig.Flush();
        Assert.Equal(0, TakeOvers);
    }

    [Fact]
    public void Always_take_over_at_start_is_hidden_without_L_Connect_unless_it_is_on()
    {
        Engine.LConnectInstalled = false;
        Assert.Null(_rig.Item(MenuKeys.AlwaysTakeOver));

        // Still there to turn off.
        Engine.Settings = Engine.Settings! with { Handover = HandoverMode.Auto };
        Assert.True(_rig.Item(MenuKeys.AlwaysTakeOver)!.Checked);
    }
}
