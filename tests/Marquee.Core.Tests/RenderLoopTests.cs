using Marquee.Core.Devices;
using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

[Collection("Real-time render loop")]
public class RenderLoopTests
{
    static string Fill(string color, string orientation = "landscape", int fps = 30) =>
        $"canvas: {{ orientation: {orientation}, fps: {fps} }}\nbackground:\n  - fill: \"{color}\"\n";

    static TimeSpan Seconds(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Step_renders_the_theme_and_pushes_it_through_a_file_sink()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marquee-loop-" + Guid.NewGuid().ToString("N")[..8]);
        using var folder = new TempTheme(Fill("#FF0000"));
        using var sink = new FileSinkDevice(dir);
        using var loop = new RenderLoop(sink, TestSensors.Store(), new ListLog());

        loop.SetTheme(folder.Load());
        loop.Step(TimeSpan.Zero);

        using var frame = SKImage.FromEncodedData(File.ReadAllBytes(Path.Combine(dir, "frame_000001.png")));
        Assert.Equal((1920, 480), (frame.Width, frame.Height));
        Pixels.AssertNear(SKColors.Red, Images.PixelAt(frame, 960, 240), 0);
        Assert.Equal(1, loop.FramesPushed);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Nothing_is_pushed_before_a_theme_arrives()
    {
        using var device = new RecordingDevice();
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());

        loop.Step(TimeSpan.Zero);

        Assert.Equal(0, device.Opens);
        Assert.Equal(0, device.FramesPushed);
    }

    [Fact]
    public void A_new_theme_is_swapped_in_at_the_next_frame_and_the_old_one_disposed()
    {
        using var red = new TempTheme(Fill("#FF0000"));
        using var blue = new TempTheme(Fill("#0000FF"));
        using var device = new RecordingDevice();
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        var first = red.Load();
        loop.SetTheme(first);
        loop.Step(TimeSpan.Zero);

        loop.SetTheme(blue.Load());
        Assert.False(first.IsDisposed);
        loop.Step(Seconds(0.1));

        Assert.True(first.IsDisposed);
        Pixels.AssertNear(SKColors.Blue, device.LastFrame!.GetPixel(10, 10), 0);
    }

    [Fact]
    public void A_theme_replaced_before_it_was_shown_is_disposed()
    {
        using var red = new TempTheme(Fill("#FF0000"));
        using var blue = new TempTheme(Fill("#0000FF"));
        using var device = new RecordingDevice();
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        var skipped = red.Load();

        loop.SetTheme(skipped);
        loop.SetTheme(blue.Load());
        loop.Step(TimeSpan.Zero);

        Assert.True(skipped.IsDisposed);
        Pixels.AssertNear(SKColors.Blue, device.LastFrame!.GetPixel(10, 10), 0);
    }

    [Fact]
    public void A_landscape_theme_on_a_portrait_screen_is_letterboxed_with_one_warning()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        using var device = new RecordingDevice(480, 1920);
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log);

        loop.SetTheme(folder.Load());
        loop.Step(TimeSpan.Zero);
        loop.Step(Seconds(0.1));

        var frame = device.LastFrame!;
        Assert.Equal((480, 1920), (frame.Width, frame.Height));
        Pixels.AssertNear(SKColors.Red, frame.GetPixel(240, 960), 0);
        Pixels.AssertNear(SKColors.Black, frame.GetPixel(240, 100), 0);
        var warning = Assert.Single(log.Entries);
        Assert.StartsWith("warn: Theme '", warning);
        Assert.Contains("is designed for landscape (1920×480), but the screen is set up for portrait (480×1920)", warning);
    }

    [Fact]
    public void A_matching_theme_is_not_warned_about()
    {
        using var folder = new TempTheme(Fill("#FF0000", "portrait"));
        using var device = new RecordingDevice(480, 1920);
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log);

        loop.SetTheme(folder.Load());
        loop.Step(TimeSpan.Zero);

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void A_busy_screen_is_retried_after_the_reconnect_delay()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        using var device = new RecordingDevice { OpenFault = attempt => attempt == 1 ? new DeviceBusyException("The screen is in use by another program.") : null };
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log, lconnect: FakeLConnectStatus.Detector());
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);
        loop.Step(Seconds(1));
        Assert.Equal((1, 0), (device.Opens, device.FramesPushed));

        loop.Step(Seconds(2));
        Assert.Equal((2, 1), (device.Opens, device.FramesPushed));
        Assert.StartsWith("error: The screen is unavailable: The screen is in use by another program.", log.Entries[0]);
        Assert.Equal("info: Connected to Recording device.", log.Entries[1]);
    }

    [Fact]
    public void A_screen_held_by_the_L_Connect_service_logs_the_tray_handover_in_one_line()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        using var device = new RecordingDevice { OpenFault = _ => new DeviceBusyException("The screen is in use by another program.") };
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log, lconnect: FakeLConnectStatus.Detector(service: true, app: true));
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(
            "error: The screen is unavailable: The screen is in use by another program. " +
            LConnectDetector.LogHint(LConnectState.Service) + " Retrying every 2 s.",
            entry);
        Assert.Contains("stop-service option", entry);
        Assert.DoesNotContain('\n', entry);
    }

    [Fact]
    public void A_screen_held_by_the_L_Connect_app_alone_logs_the_short_quit_hint()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        using var device = new RecordingDevice { OpenFault = _ => new DeviceBusyException("The screen is in use by another program.") };
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log, lconnect: FakeLConnectStatus.Detector(app: true));
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);

        Assert.Equal(
            ["error: The screen is unavailable: The screen is in use by another program. L-Connect 3 is running; quit it from its tray icon. Retrying every 2 s."],
            log.Entries);
    }

    [Fact]
    public void A_busy_screen_without_L_Connect_logs_no_hint()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        using var device = new RecordingDevice { OpenFault = _ => new DeviceBusyException("The screen is in use by another program.") };
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log, lconnect: FakeLConnectStatus.Detector());
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);

        Assert.Equal(["error: The screen is unavailable: The screen is in use by another program. Retrying every 2 s."], log.Entries);
    }

    [Fact]
    public void Only_a_busy_screen_asks_about_L_Connect()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        using var device = new RecordingDevice { OpenFault = _ => new IOException("USB write failed") };
        var status = new FakeLConnectStatus(service: true);
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log, lconnect: new LConnectDetector(status));
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);

        Assert.Equal(0, status.Queries);
        Assert.Equal(["error: The screen is unavailable: USB write failed Retrying every 2 s."], log.Entries);
    }

    [Fact]
    public void A_failed_push_closes_the_device_and_reconnects_later()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        using var device = new RecordingDevice { PushFault = attempt => attempt is 2 or 3 ? new IOException("USB write failed") : null };
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log);
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);
        loop.Step(Seconds(0.1));
        Assert.Equal(1, device.Closes);

        loop.Step(Seconds(1));
        Assert.Equal(1, device.Opens);

        loop.Step(Seconds(2.1));
        Assert.Equal((2, 2), (device.Opens, device.Closes));

        loop.Step(Seconds(4.2));
        Assert.Equal((3, 2), (device.Opens, device.FramesPushed));
        // The same failure twice in a row is logged once.
        Assert.Equal(
            ["error: The screen is unavailable: USB write failed Retrying every 2 s.", "info: Connected to Recording device."],
            log.Entries);
    }

    [Fact]
    public void A_render_failure_is_logged_once_and_never_announced_as_a_reconnect()
    {
        using var folder = new TempTheme(Fill("#FF0000", fps: 30));
        using var device = new RecordingDevice();
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log);
        var calls = 0;
        loop.FrameRendered += _ =>
        {
            if (++calls == 1) throw new InvalidOperationException("boom");
        };
        loop.SetTheme(folder.Load());

        loop.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (device.FramesPushed < 3 && DateTime.UtcNow < deadline) Thread.Sleep(10);
        loop.Stop();

        Assert.True(device.FramesPushed >= 3);
        Assert.Equal(["error: Sending a frame failed: boom"], log.Entries);
    }

    [Fact]
    public void Stats_report_the_frame_rate_over_the_last_second()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        using var device = new RecordingDevice();
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        loop.SetTheme(folder.Load());

        for (var i = 0; i <= 60; i++)
            loop.Step(Seconds(i / 30.0));

        Assert.Equal(30, loop.Stats.Fps, 0.5);
        Assert.True(loop.Stats.RenderMs > 0);
    }

    [Fact]
    public void Warns_once_when_frames_come_slower_than_the_theme_asks()
    {
        using var folder = new TempTheme(Fill("#FF0000", fps: 30));
        using var device = new RecordingDevice();
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log);
        loop.SetTheme(folder.Load());

        for (var i = 0; i <= 100; i++)
            loop.Step(Seconds(i * 0.05));

        var warning = Assert.Single(log.Entries);
        Assert.StartsWith("warn: Running at 20 fps instead of 30: a frame takes ", warning);
    }

    [Fact]
    public void Fps_comes_from_the_override_or_the_theme_and_is_clamped()
    {
        using var folder = new TempTheme(Fill("#FF0000", fps: 15));
        using var device = new RecordingDevice();
        using var themed = new RenderLoop(device, TestSensors.Store(), new ListLog());
        using var overridden = new RenderLoop(device, TestSensors.Store(), new ListLog(), fpsOverride: 90);

        Assert.Equal(30, themed.Fps);
        themed.SetTheme(folder.Load());
        themed.Step(TimeSpan.Zero);

        Assert.Equal(15, themed.Fps);
        Assert.Equal(60, overridden.Fps);
    }

    [Fact]
    public void FrameRendered_sees_every_frame_even_while_the_screen_is_away()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        using var device = new RecordingDevice { OpenFault = _ => new DeviceBusyException("busy") };
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog(), lconnect: FakeLConnectStatus.Detector());
        var sizes = new List<(int, int)>();
        loop.FrameRendered += frame => sizes.Add((frame.Width, frame.Height));
        loop.SetTheme(folder.Load());

        loop.Step(TimeSpan.Zero);
        loop.Step(Seconds(0.1));

        Assert.Equal([(1920, 480), (1920, 480)], sizes);
    }

    [Fact]
    public void Start_runs_frames_on_its_own_thread_until_stopped()
    {
        using var folder = new TempTheme(Fill("#FF0000", fps: 30));
        using var device = new RecordingDevice();
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        loop.SetTheme(folder.Load());

        loop.Start();
        Thread.Sleep(700);
        loop.Stop();
        var pushed = device.FramesPushed;
        Thread.Sleep(100);

        Assert.InRange(pushed, 8, 30);
        Assert.Equal(pushed, device.FramesPushed);
        Assert.Equal((1, 1), (device.Opens, device.Closes));
    }

    [Fact]
    public void Stop_closes_the_screen_and_Start_opens_it_again_with_the_same_theme()
    {
        using var folder = new TempTheme(Fill("#FF0000", fps: 30));
        using var device = new RecordingDevice();
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        loop.SetTheme(folder.Load());
        loop.Start();
        Assert.True(Wait.Until(() => device.FramesPushed >= 30));

        loop.Stop();
        Assert.Equal((1, 1), (device.Opens, device.Closes));
        var pushed = device.FramesPushed;
        loop.Start();

        Assert.True(Wait.Until(() => device.FramesPushed >= pushed + 3), "No frames after the restart.");
        loop.Stop();
        // The theme asks for 30 fps, and on a busy runner uneven frame timing has made this read as high as about 50. A
        // clock reset by Stop would start the new frames' timestamps below the old ones still in the window, and the
        // rate would read in the hundreds or as 0.
        Assert.InRange(loop.Stats.Fps, 1, 100);
        Assert.Equal((2, 2), (device.Opens, device.Closes));
        Pixels.AssertNear(SKColors.Red, device.LastFrame!.GetPixel(10, 10), 0);
    }

    [Fact]
    public void FpsOverride_can_be_changed_and_cleared_at_any_time()
    {
        using var folder = new TempTheme(Fill("#FF0000", fps: 15));
        using var device = new RecordingDevice();
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog(), fpsOverride: 20);
        loop.SetTheme(folder.Load());
        loop.Step(TimeSpan.Zero);
        Assert.Equal((20, 20), (loop.FpsOverride, loop.Fps));

        loop.FpsOverride = 45;
        Assert.Equal(45, loop.Fps);

        loop.FpsOverride = null;
        Assert.Equal(15, loop.Fps);
    }

    [Fact]
    public void Step_cannot_be_used_while_the_loop_is_running()
    {
        using var device = new RecordingDevice();
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        loop.Start();
        try
        {
            Assert.Throws<InvalidOperationException>(() => loop.Step(Seconds(0)));
        }
        finally
        {
            loop.Stop();
        }

        loop.Step(Seconds(0)); // fine again once stopped
    }
}
