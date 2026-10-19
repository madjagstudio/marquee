using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

/// Closing the running trays before the installer replaces or removes their files, through fake processes: no test here
/// signals, waits for or kills a real process.
public class TrayStopperTests
{
    const string Tray = @"C:\Program Files\Marquee\MarqueeTray.exe";
    static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    [Fact]
    public void Each_tray_is_asked_to_quit_and_given_time_to()
    {
        var first = new FakeTray(1, hasQuitEvent: true, quitsWhenAsked: true);
        var second = new FakeTray(2, hasQuitEvent: true, quitsWhenAsked: true);
        var processes = new FakeTrayProcesses(first, second);

        var result = TrayStopper.Stop(Tray, processes, Grace);

        Assert.Equal(new TrayStopResult(Quit: 2, Killed: 0), result);
        Assert.Equal(Tray, processes.AskedFor);
        Assert.All([first, second], tray =>
        {
            Assert.True(tray.Asked);
            Assert.False(tray.Killed);
            Assert.True(tray.Waited > TimeSpan.Zero && tray.Waited <= Grace);
            Assert.True(tray.Disposed);
        });
    }

    [Fact]
    public void A_tray_that_does_not_quit_in_time_is_ended()
    {
        var stuck = new FakeTray(1, hasQuitEvent: true, quitsWhenAsked: false);

        var result = TrayStopper.Stop(Tray, new FakeTrayProcesses(stuck), Grace);

        Assert.Equal(new TrayStopResult(Quit: 0, Killed: 1), result);
        Assert.True(stuck.Asked);
        Assert.True(stuck.Killed);
    }

    /// A tray without the quit event (one that can't hear it) is ended at once: waiting for it would only delay the
    /// install.
    [Fact]
    public void A_tray_with_no_quit_event_is_ended_without_waiting()
    {
        var old = new FakeTray(1, hasQuitEvent: false, quitsWhenAsked: false);

        var result = TrayStopper.Stop(Tray, new FakeTrayProcesses(old), Grace);

        Assert.Equal(new TrayStopResult(Quit: 0, Killed: 1), result);
        Assert.Equal(TimeSpan.Zero, old.Waited);
        Assert.True(old.Killed);
    }

    [Fact]
    public void No_tray_running_is_nothing_to_do()
    {
        Assert.Equal(new TrayStopResult(Quit: 0, Killed: 0), TrayStopper.Stop(Tray, new FakeTrayProcesses(), Grace));
    }

    /// Every other tray is still closed, so as few files as possible are left in use, and the error comes at the end.
    [Fact]
    public void A_tray_that_cannot_be_ended_is_an_error_after_every_other_tray_is_closed()
    {
        var stuck = new FakeTray(1, hasQuitEvent: false, quitsWhenAsked: false, killFails: true);
        var other = new FakeTray(2, hasQuitEvent: true, quitsWhenAsked: true);
        var deaf = new FakeTray(3, hasQuitEvent: false, quitsWhenAsked: false);

        var error = Assert.Throws<System.ComponentModel.Win32Exception>(() => TrayStopper.Stop(Tray, new FakeTrayProcesses(stuck, other, deaf), Grace));

        Assert.Equal(5, error.NativeErrorCode);
        Assert.True(other.Waited > TimeSpan.Zero);
        Assert.True(deaf.Killed);
        Assert.All([stuck, other, deaf], tray => Assert.True(tray.Disposed));
    }

    /// The tray waits on Local\…, its own session's name; the installer runs in session 0 and reaches the tray's
    /// session by its number.
    [Fact]
    public void The_quit_event_is_the_trays_own_and_reachable_from_another_session()
    {
        Assert.StartsWith(@"Local\Marquee-Tray-Quit-S-1-5-", TrayNames.Quit);
        Assert.Equal(@"Session\2\Marquee-Tray-Quit-S-1-5-21-1-2-3-1001", TrayNames.QuitIn(2, "S-1-5-21-1-2-3-1001"));
    }

    [Fact]
    public void The_real_processes_hold_no_tray_at_a_path_nothing_runs_from()
    {
        using var folder = new TempDir();

        Assert.Empty(new TrayProcesses().Running(folder.File("MarqueeTray.exe")));
    }
}
