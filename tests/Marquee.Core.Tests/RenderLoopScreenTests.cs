using System.Runtime.CompilerServices;
using Marquee.Core.Devices;
using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// What the render loop tells the L-Connect handover: when the screen comes up busy (once per busy episode), when it
/// opens and closes, and how to wait for it or retry it at once.
[Collection("Real-time render loop")]
public class RenderLoopScreenTests
{
    const string Theme = "canvas: { orientation: landscape, fps: 30 }\nbackground:\n  - fill: \"#FF0000\"\n";

    static TimeSpan Seconds(double s) => TimeSpan.FromSeconds(s);

    static DeviceBusyException Busy() => new("The screen is in use by another program.");

    [Fact]
    public void A_busy_screen_is_reported_once_per_episode_with_who_holds_it()
    {
        using var folder = new TempTheme(Theme);
        using var device = new RecordingDevice { OpenFault = _ => Busy() };
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog(), lconnect: FakeLConnectStatus.Detector(service: true));
        var busy = new List<LConnectState>();
        loop.ScreenBusy += busy.Add;
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);
        loop.Step(Seconds(2));
        loop.Step(Seconds(4));

        Assert.Equal(3, device.Opens);
        Assert.Equal([LConnectState.Service], busy);
    }

    [Fact]
    public void The_episode_ends_when_a_frame_gets_through_and_a_later_busy_screen_starts_another()
    {
        using var folder = new TempTheme(Theme);
        using var device = new RecordingDevice
        {
            OpenFault = attempt => attempt <= 2 ? Busy() : null,
            PushFault = attempt => attempt == 2 ? Busy() : null,
        };
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog(), lconnect: FakeLConnectStatus.Detector(app: true));
        var busy = new List<LConnectState>();
        loop.ScreenBusy += busy.Add;
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);
        loop.Step(Seconds(2));
        Assert.Single(busy);

        loop.Step(Seconds(4)); // opens, and the frame gets through
        loop.Step(Seconds(4.1)); // busy again

        Assert.Equal([LConnectState.AppOnly, LConnectState.AppOnly], busy);
    }

    [Fact]
    public void Other_screen_errors_are_not_a_busy_screen()
    {
        using var folder = new TempTheme(Theme);
        using var device = new RecordingDevice { OpenFault = _ => new IOException("USB write failed") };
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog(), lconnect: FakeLConnectStatus.Detector(service: true));
        var busy = new List<LConnectState>();
        loop.ScreenBusy += busy.Add;
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);

        Assert.Empty(busy);
    }

    [Fact]
    public void The_screen_is_announced_open_when_the_first_frame_gets_through_and_closed_when_it_goes()
    {
        using var folder = new TempTheme(Theme);
        using var device = new RecordingDevice { PushFault = attempt => attempt == 2 ? new IOException("USB write failed") : null };
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        var changes = new List<(bool Open, int Closes)>();
        loop.ScreenOpenChanged += open => changes.Add((open, device.Closes));
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);
        loop.Step(Seconds(0.1)); // the push fails and the screen closes
        loop.Step(Seconds(2.1)); // it opens again
        loop.Step(Seconds(2.2)); // still open: no second announcement

        // The close is announced before the screen is let go of.
        Assert.Equal([(true, 0), (false, 0), (true, 1)], changes);
    }

    [Fact]
    public void A_screen_that_opens_but_takes_no_frame_is_never_announced()
    {
        using var folder = new TempTheme(Theme);
        using var device = new RecordingDevice { PushFault = _ => new IOException("USB write failed") };
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        var changes = new List<bool>();
        loop.ScreenOpenChanged += changes.Add;
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);

        Assert.Equal((1, 1), (device.Opens, device.Closes));
        Assert.Empty(changes);
    }

    [Fact]
    public void RetryNow_makes_the_next_frame_try_the_screen_without_waiting_out_the_reconnect_delay()
    {
        using var folder = new TempTheme(Theme);
        using var device = new RecordingDevice { OpenFault = attempt => attempt == 1 ? Busy() : null };
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog(), lconnect: FakeLConnectStatus.Detector());
        var changes = new List<bool>();
        loop.ScreenOpenChanged += changes.Add;
        loop.SetTheme(folder.Load());
        loop.Step(TimeSpan.Zero);

        loop.Step(Seconds(0.5));
        Assert.Equal(1, device.Opens);

        loop.RetryNow();
        loop.Step(Seconds(0.6));
        Assert.Equal((2, 1), (device.Opens, device.FramesPushed));
        Assert.Equal([true], changes);

        // It was for one attempt only.
        device.PushFault = attempt => attempt == 2 ? new IOException("USB write failed") : null;
        loop.Step(Seconds(0.7));
        loop.Step(Seconds(1.0));
        Assert.Equal(2, device.Opens);
    }

    [Fact]
    public void The_sending_thread_announces_the_screen_once_it_is_free()
    {
        using var folder = new TempTheme(Theme);
        var busy = new StrongBox<bool>(true);
        using var device = new RecordingDevice { OpenFault = _ => Volatile.Read(ref busy.Value) ? Busy() : null };
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog(), lconnect: FakeLConnectStatus.Detector(service: true));
        var episodes = 0;
        using var opened = new ManualResetEventSlim();
        using var closed = new ManualResetEventSlim();
        loop.ScreenBusy += _ => Interlocked.Increment(ref episodes);
        loop.ScreenOpenChanged += open => (open ? opened : closed).Set();
        loop.SetTheme(folder.Load());
        loop.Start();
        try
        {
            Assert.True(Wait.Until(() => Volatile.Read(ref episodes) == 1), "The busy screen was never reported.");
            Assert.False(opened.IsSet);

            Volatile.Write(ref busy.Value, false);
            loop.RetryNow();

            Assert.True(opened.Wait(TimeSpan.FromSeconds(15)), "The screen was never announced open.");
        }
        finally
        {
            loop.Stop();
        }

        // Stop closes the screen and says so.
        Assert.True(closed.IsSet);
        Assert.Equal(1, Volatile.Read(ref episodes));
    }

    [Fact]
    public void A_restart_after_Stop_reports_a_screen_that_is_still_busy_again()
    {
        using var folder = new TempTheme(Theme);
        using var device = new RecordingDevice { OpenFault = _ => Busy() };
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog(), lconnect: FakeLConnectStatus.Detector(service: true));
        var episodes = 0;
        loop.ScreenBusy += _ => Interlocked.Increment(ref episodes);
        loop.SetTheme(folder.Load());

        loop.Start();
        Assert.True(Wait.Until(() => Volatile.Read(ref episodes) == 1), "The busy screen was never reported.");
        loop.Stop();
        loop.Start();
        Assert.True(Wait.Until(() => Volatile.Read(ref episodes) == 2), "The resumed loop didn't report the busy screen.");
        loop.Stop();
    }
}
