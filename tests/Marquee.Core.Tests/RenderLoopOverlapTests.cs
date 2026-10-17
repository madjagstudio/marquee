using Marquee.Core.Devices;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Start() renders and sends on two threads: the next frame is drawn while the last one goes to the screen.
public class RenderLoopOverlapTests
{
    const string Theme = "canvas: { orientation: landscape, fps: 30 }\nbackground:\n  - fill: \"#FF0000\"\n";

    /// A screen that takes `pushMs` per frame and records which threads touch it, and whether two pushes ever overlap.
    sealed class SlowDevice(int pushMs) : IDisplayDevice
    {
        int _inPush;

        public DeviceInfo Info { get; } = new("Slow device", 1920, 480);
        public HashSet<int> Threads { get; } = [];
        public int Frames;
        public bool Overlapped;

        public void Open() => Touch();

        public void PushFrame(SKImage frame)
        {
            Touch();
            if (Interlocked.Increment(ref _inPush) > 1) Overlapped = true;
            Thread.Sleep(pushMs);
            Interlocked.Decrement(ref _inPush);
            Interlocked.Increment(ref Frames);
        }

        public void SetBrightness(int percent) => Touch();
        public void Close() => Touch();
        public void Dispose() { }

        void Touch()
        {
            lock (Threads) Threads.Add(Environment.CurrentManagedThreadId);
        }
    }

    /// A store whose snapshots take `ms`, which makes every frame that long to render.
    static SensorStore SlowSensors(int ms) => new(() =>
    {
        Thread.Sleep(ms);
        return TestSensors.Time;
    });

    [Fact]
    public void Rendering_overlaps_sending()
    {
        // 25 ms to render plus 25 ms to send: one after the other that is 20 fps; overlapped, the 30 fps target holds.
        using var folder = new TempTheme(Theme);
        var device = new SlowDevice(25);
        using var loop = new RenderLoop(device, SlowSensors(25), new ListLog());
        loop.SetTheme(folder.Load());

        loop.Start();
        Thread.Sleep(2000);
        loop.Stop();

        Assert.True(device.Frames >= 50, $"only {device.Frames} frames in 2 s");
    }

    [Fact]
    public void Only_one_thread_ever_touches_the_device()
    {
        using var folder = new TempTheme(Theme);
        var device = new SlowDevice(5);
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        loop.SetTheme(folder.Load());

        loop.Start();
        Thread.Sleep(500);
        loop.Stop();

        Assert.True(device.Frames > 0);
        Assert.False(device.Overlapped);
        Assert.Single(device.Threads);
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, device.Threads);
    }

    [Fact]
    public void A_slow_screen_gets_the_newest_frame_and_the_rest_are_dropped()
    {
        // Rendering keeps the theme's pace while the screen takes 100 ms a frame: the screen gets ~10 fps, not a backlog.
        using var folder = new TempTheme(Theme);
        var device = new SlowDevice(100);
        var rendered = 0;
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        loop.FrameRendered += _ => Interlocked.Increment(ref rendered);
        loop.SetTheme(folder.Load());

        loop.Start();
        Thread.Sleep(1000);
        loop.Stop();
        var afterStop = device.Frames;
        Thread.Sleep(300);

        Assert.InRange(device.Frames, 5, 12);
        Assert.Equal(afterStop, device.Frames); // nothing queued up keeps going after Stop
        Assert.Equal(device.Frames, rendered);
    }

    [Fact]
    public void Stats_report_render_and_send_times_separately()
    {
        using var folder = new TempTheme(Theme);
        var device = new SlowDevice(20);
        using var loop = new RenderLoop(device, SlowSensors(10), new ListLog());
        loop.SetTheme(folder.Load());

        loop.Start();
        Thread.Sleep(1500);
        loop.Stop();

        // Upper bounds are loose: a busy CI machine oversleeps.
        Assert.InRange(loop.Stats.RenderMs, 9, 100);
        Assert.InRange(loop.Stats.PushMs, 19, 100);
        Assert.True(loop.Stats.Fps > 0);
    }
}
