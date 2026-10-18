using Marquee.Core.Devices;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// The render loop tests that depend on real time (threads, sleeps, frame rates) run apart from the rest of the suite,
/// so the CPU-heavy Skia tests don't starve them.
[CollectionDefinition("Real-time render loop", DisableParallelization = true)]
public class RealTimeRenderLoopCollection;

/// Start() renders and sends on two threads: the next frame is drawn while the last one goes to the screen.
[Collection("Real-time render loop")]
public class RenderLoopOverlapTests
{
    const string Theme = "canvas: { orientation: landscape, fps: 30 }\nbackground:\n  - fill: \"#FF0000\"\n";

    /// What the sensors and the screen share, so a test can see how the two threads interleave.
    sealed class Probe
    {
        public int Rendering; // renders in progress right now
        public int Renders; // renders started in total
    }

    /// A screen that takes `pushMs` per frame and records which threads touch it, whether two pushes ever overlap, whether
    /// a render was in progress during a push, and how many renders had started when each push began.
    sealed class SlowDevice(int pushMs, Probe? probe = null) : IDisplayDevice
    {
        int _inPush;
        int _sawRender;

        public DeviceInfo Info { get; } = new("Slow device", 1920, 480);
        public HashSet<int> Threads { get; } = [];
        public List<int> RendersAtPushStart { get; } = [];
        public int Frames;
        public bool Overlapped;
        public bool SawRenderDuringPush => Volatile.Read(ref _sawRender) != 0;

        public void Open() => Touch();

        public void PushFrame(SKImage frame)
        {
            Touch();
            if (probe is not null) lock (RendersAtPushStart) RendersAtPushStart.Add(Volatile.Read(ref probe.Renders));
            if (Interlocked.Increment(ref _inPush) > 1) Overlapped = true;
            NoteRender();
            Thread.Sleep(pushMs);
            NoteRender();
            Interlocked.Decrement(ref _inPush);
            Interlocked.Increment(ref Frames);
        }

        public void SetBrightness(int percent) => Touch();
        public void Close() => Touch();
        public void Dispose() { }

        void NoteRender()
        {
            if (probe is not null && Volatile.Read(ref probe.Rendering) != 0) Volatile.Write(ref _sawRender, 1);
        }

        void Touch()
        {
            lock (Threads) Threads.Add(Environment.CurrentManagedThreadId);
        }
    }

    /// A store whose snapshots take `ms`, which makes every frame that long to render. The probe shows when that is going on.
    static SensorStore SlowSensors(int ms, Probe? probe = null) => new(() =>
    {
        if (probe is not null)
        {
            Interlocked.Increment(ref probe.Renders);
            Interlocked.Increment(ref probe.Rendering);
        }

        try
        {
            Thread.Sleep(ms);
            return TestSensors.Time;
        }
        finally
        {
            if (probe is not null) Interlocked.Decrement(ref probe.Rendering);
        }
    });

    [Fact]
    public void Rendering_overlaps_sending()
    {
        // 25 ms to render and 25 ms to send. What matters is that a frame is being drawn while another goes to the screen,
        // not how many frames get through: a loaded CI machine stalls for a while.
        using var folder = new TempTheme(Theme);
        var probe = new Probe();
        var device = new SlowDevice(25, probe);
        using var loop = new RenderLoop(device, SlowSensors(25, probe), new ListLog());
        loop.SetTheme(folder.Load());

        loop.Start();
        Thread.Sleep(2000);
        loop.Stop();

        Assert.True(device.SawRenderDuringPush, "no frame was ever rendered while another was being sent");
        Assert.True(device.Frames > 10, $"only {device.Frames} frames in 2 s");
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
        // Rendering keeps the theme's pace (30 fps) while the screen takes 100 ms a frame (10 fps): the excess frames are
        // dropped, not queued. A render count that outruns the pushes proves it, and the last frame the screen got was one
        // of the latest renders, not one from the back of a queue.
        using var folder = new TempTheme(Theme);
        var probe = new Probe();
        var device = new SlowDevice(100, probe);
        using var loop = new RenderLoop(device, SlowSensors(0, probe), new ListLog());
        loop.SetTheme(folder.Load());

        loop.Start();
        Thread.Sleep(1000);
        loop.Stop();
        var pushes = device.Frames;
        var renders = Volatile.Read(ref probe.Renders);
        Thread.Sleep(300);

        Assert.True(pushes >= 2, $"only {pushes} pushes");
        Assert.True(renders >= 2 * pushes, $"{renders} renders for {pushes} pushes: frames were queued, not dropped");
        Assert.Equal(pushes, device.Frames); // nothing queued up keeps going after Stop
        // A push starts with the newest frame, so the render count at that moment is (about) that frame's index.
        int lastPushedRender;
        lock (device.RendersAtPushStart) lastPushedRender = device.RendersAtPushStart[^1];
        Assert.True(lastPushedRender >= renders - 5, $"the last frame sent came from render {lastPushedRender} of {renders}");
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
