using System.Diagnostics;
using Marquee.Core.Devices;
using Marquee.Core.Devices.LedRing;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Host.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Host.Tests;

/// The LED ring's sender. Most tests drive Tick with made-up times, so the rate, the once-a-second resend and the
/// retry delays are exact; the threaded tests only check what the thread adds (Start, Pause and the rate ceiling).
public class RingSenderTests : IDisposable
{
    const string Unavailable = "warn: The LED ring is unavailable: The device is gone. Retrying every 2 s.";
    const string Connected = "info: Connected to the LED ring.";
    const string NotFound = "info: No LED ring was found; Marquee keeps looking for one.";

    static readonly SKColor Red = new(0xFF, 0, 0), Blue = new(0, 0, 0xFF);

    readonly FakeRing _ring = new();
    readonly SensorStore _sensors = new();
    readonly ListLog _log = new();
    readonly RingSender _sender;
    volatile Func<ILedRing?> _find;
    int _finds;

    public RingSenderTests()
    {
        _find = () => _ring;
        _sender = new RingSender(() =>
        {
            Interlocked.Increment(ref _finds);
            return _find();
        }, _sensors, _log);
    }

    public void Dispose() => _sender.Dispose();

    static RingSpec Static(SKColor color) => RingSpec.Default with { Color = color, Brightness = 1 };

    static RingSpec Ring(string block) => LayoutParser.Parse("canvas: { orientation: landscape }\n" + block).Ring!;

    static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    SKColor[] Expected(RingSpec spec, TimeSpan time)
    {
        var colors = new SKColor[RingRenderer.LedCount];
        RingRenderer.Render(spec, _sensors.Snapshot(), time, colors);
        return colors;
    }

    [Fact]
    public void Each_tick_renders_the_spec_and_sends_all_60_colors()
    {
        var spin = Ring("ring: { color: \"#3A7BFF\", effect: spin }\n");
        _sender.Spec = spin;

        foreach (var time in new[] { Ms(0), Ms(33), Ms(67) })
            _sender.Tick(time);

        Assert.Equal(1, _ring.Opens);
        Assert.Equal([Expected(spin, Ms(0)), Expected(spin, Ms(33)), Expected(spin, Ms(67))], _ring.Frames);
        Assert.All(_ring.Frames, frame => Assert.Equal(60, frame.Length));
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public void The_rings_when_rules_follow_the_sensors()
    {
        _sender.Spec = Ring("""
            ring:
              color: "#0000FF"
              brightness: 100
              source: cpu.temp
              when:
                - { above: 85, color: "#FF0000" }
            """);

        _sensors.Set("cpu.temp", 50);
        _sender.Tick(Ms(0));
        Assert.Equal(Blue, _ring.LastFrame![0]);

        _sensors.Set("cpu.temp", 90);
        _sender.Tick(Ms(33));
        Assert.Equal(Red, _ring.LastFrame![0]);
    }

    [Fact]
    public void An_unchanged_frame_is_sent_again_only_once_a_second()
    {
        _sender.Spec = Static(Red);

        _sender.Tick(Ms(0));
        for (var time = 33; time < 1000; time += 33)
            _sender.Tick(Ms(time));
        Assert.Equal(1, _ring.FrameCount);

        _sender.Tick(Ms(1000));
        Assert.Equal(2, _ring.FrameCount);
    }

    [Fact]
    public void A_new_spec_shows_at_the_next_tick()
    {
        _sender.Spec = Static(Red);
        _sender.Tick(Ms(0));

        _sender.Spec = Static(Blue);
        _sender.Tick(Ms(10));

        Assert.Equal(2, _ring.FrameCount);
        Assert.All(_ring.LastFrame!, color => Assert.Equal(Blue, color));
    }

    [Fact]
    public void Off_sends_black_once_closes_the_ring_and_then_sends_nothing()
    {
        _sender.Spec = Static(Red);
        _sender.Tick(Ms(0));

        _sender.Spec = null;
        _sender.Tick(Ms(33));
        _sender.Tick(Ms(1000));
        _sender.Tick(Ms(5000));

        Assert.Equal(2, _ring.FrameCount);
        Assert.True(FakeRing.IsBlack(_ring.LastFrame));
        Assert.Equal((1, 1, false), (_ring.Opens, _ring.Closes, _ring.IsOpen));

        // Back on: the ring opens again and lights at once.
        _sender.Spec = Static(Red);
        _sender.Tick(Ms(5033));
        Assert.Equal((2, true), (_ring.Opens, _ring.IsOpen));
        Assert.Equal(Red, _ring.LastFrame![0]);
    }

    [Fact]
    public void Off_from_the_start_darkens_a_ring_an_earlier_run_left_lit()
    {
        _sender.Spec = null;

        _sender.Tick(Ms(0));
        _sender.Tick(Ms(2000));

        Assert.True(FakeRing.IsBlack(Assert.Single(_ring.Frames)));
        Assert.False(_ring.IsOpen);
    }

    [Fact]
    public void A_ring_that_fails_is_let_go_found_again_after_2_seconds_and_reported_once()
    {
        _sender.Spec = Static(Red);
        _ring.SendFault = new IOException("The device is gone.");

        _sender.Tick(Ms(0));
        Assert.Equal((1, 1, false), (_ring.Opens, _ring.Closes, _ring.IsOpen));

        _sender.Tick(Ms(1999));
        Assert.Equal(1, _ring.Opens);

        _sender.Tick(Ms(2000));
        Assert.Equal(2, _ring.Opens);
        Assert.Equal(2, _finds); // found again, since a ring plugged back in may have a new device path
        Assert.Equal([Unavailable], _log.Entries);

        _ring.SendFault = null;
        _sender.Tick(Ms(4000));
        Assert.Equal(Red, _ring.LastFrame![0]);
        Assert.Equal([Unavailable, Connected], _log.Entries);
    }

    [Fact]
    public void A_ring_another_program_holds_is_tried_again_after_2_seconds()
    {
        _sender.Spec = Static(Red);
        _ring.OpenFault = new DeviceBusyException("The LED ring is in use by another program.");

        _sender.Tick(Ms(0));
        _sender.Tick(Ms(1000));
        Assert.Equal(1, _ring.Opens);
        _sender.Tick(Ms(2000));
        Assert.Equal(2, _ring.Opens);

        _ring.OpenFault = null;
        _sender.Tick(Ms(4000));

        Assert.Equal(Red, Assert.Single(_ring.Frames)[0]);
        const string Busy =
            "warn: The LED ring is unavailable: The LED ring is in use by another program. Retrying every 2 s.";
        Assert.Equal([Busy, Connected], _log.Entries);
    }

    [Fact]
    public void With_no_ring_nothing_is_sent_and_it_looks_again_every_5_seconds_saying_so_once()
    {
        _find = () => null;
        _sender.Spec = Static(Red);

        foreach (var time in new[] { 0, 33, 1000, 4999 })
            _sender.Tick(Ms(time));
        Assert.Equal(1, _finds);
        _sender.Tick(Ms(5000));
        _sender.Tick(Ms(10000));
        Assert.Equal(3, _finds);
        Assert.Equal([NotFound], _log.Entries);

        _find = () => _ring; // plugged in
        _sender.Tick(Ms(15000));

        Assert.Equal(Red, Assert.Single(_ring.Frames)[0]);
        Assert.Equal([NotFound, Connected], _log.Entries);
    }

    [Fact]
    public void Looking_for_the_ring_failing_counts_as_the_ring_failing()
    {
        _find = () => throw new IOException("The device is gone.");
        _sender.Spec = Static(Red);

        _sender.Tick(Ms(0));
        _sender.Tick(Ms(2000));

        Assert.Equal(2, _finds);
        Assert.Equal([Unavailable], _log.Entries);
    }

    [Fact]
    public void Dispose_sends_black_and_closes_the_ring_and_can_be_repeated()
    {
        _sender.Spec = Static(Red);
        _sender.Tick(Ms(0));

        _sender.Dispose();
        _sender.Dispose();

        Assert.True(FakeRing.IsBlack(_ring.LastFrame));
        Assert.Equal(2, _ring.FrameCount);
        Assert.False(_ring.IsOpen);
    }

    [Fact]
    public void Dispose_darkens_a_ring_that_failed_while_lit()
    {
        _sender.Spec = Static(Red);
        _sender.Tick(Ms(0));
        _ring.SendFault = new IOException("The device is gone.");
        _sender.Tick(Ms(1000)); // the once-a-second resend fails, and the ring is let go still red
        _ring.SendFault = null;

        _sender.Dispose();

        Assert.True(FakeRing.IsBlack(_ring.LastFrame));
        Assert.Equal(2, _ring.Opens);
        Assert.False(_ring.IsOpen);
    }

    [Fact]
    public void Dispose_darkens_a_ring_it_never_opened()
    {
        // A paused start never starts the sender, but the ring may still be lit from an earlier run or a crash.
        _sender.Spec = Static(Red);

        _sender.Dispose();

        Assert.True(FakeRing.IsBlack(Assert.Single(_ring.Frames)));
        Assert.False(_ring.IsOpen);
    }

    [Fact]
    public void Darkening_a_ring_that_is_not_there_or_will_not_open_tries_once_and_says_nothing()
    {
        _sender.Spec = Static(Red);
        _ring.OpenFault = new DeviceBusyException("The LED ring is in use by another program.");

        _sender.Pause();
        _sender.Dispose();

        Assert.Equal(2, _finds); // once for each, with no waiting between tries
        Assert.Empty(_ring.Frames);
        Assert.False(_ring.IsOpen);
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public void A_ring_known_to_be_dark_is_not_looked_for_again()
    {
        _sender.Spec = null;
        _sender.Tick(Ms(0)); // `ring: off` sends black once and lets the ring go

        _sender.Pause();
        _sender.Dispose();

        Assert.Equal(1, _finds);
        Assert.Single(_ring.Frames);
    }

    [Fact]
    public void RetryNow_has_the_next_tick_try_the_ring_at_once_and_only_that_tick()
    {
        _sender.Spec = Static(Red);
        _ring.OpenFault = new DeviceBusyException("The LED ring is in use by another program.");
        _sender.Tick(Ms(0));
        Assert.Equal(1, _ring.Opens);

        _sender.RetryNow();
        _sender.Tick(Ms(100));
        Assert.Equal(2, _ring.Opens);

        _sender.Tick(Ms(200)); // that try failed too, so the 2 s wait starts again
        Assert.Equal(2, _ring.Opens);

        _ring.OpenFault = null;
        _sender.RetryNow();
        _sender.Tick(Ms(300));
        Assert.Equal(Red, Assert.Single(_ring.Frames)[0]);
    }

    [Fact]
    public void RetryNow_looks_for_a_missing_ring_at_the_next_tick()
    {
        _find = () => null;
        _sender.Spec = Static(Red);
        _sender.Tick(Ms(0));

        _find = () => _ring;
        _sender.RetryNow();
        _sender.Tick(Ms(33));

        Assert.Equal(2, _finds);
        Assert.Equal(Red, Assert.Single(_ring.Frames)[0]);
    }

    [Fact]
    public void Pause_sends_black_and_closes_the_ring_and_Start_lights_it_again()
    {
        _sender.Spec = Static(Red);
        _sender.Start();
        Assert.True(Wait.Until(() => _ring.FrameCount > 0), "Nothing reached the ring.");

        _sender.Pause();

        Assert.True(FakeRing.IsBlack(_ring.LastFrame));
        Assert.False(_ring.IsOpen);
        var frames = _ring.FrameCount;
        Thread.Sleep(200); // several ticks, had the thread still been running
        Assert.Equal(frames, _ring.FrameCount);

        _sender.Start();

        Assert.True(Wait.Until(() => _ring.LastFrame is [var first, ..] && first == Red), "The ring stayed dark.");
        Assert.Equal(2, _ring.Opens);
    }

    [Fact]
    public void The_thread_sends_at_most_30_frames_a_second()
    {
        _sender.Spec = Ring("ring: { effect: rainbow, speed: 10 }\n"); // a new frame every tick
        var clock = Stopwatch.StartNew();

        _sender.Start();
        Assert.True(Wait.Until(() => _ring.FrameCount >= 10), "The ring got too few frames.");
        _sender.Pause();
        var elapsed = clock.Elapsed.TotalSeconds;

        var lit = _ring.FrameCount - 1; // the last is Pause's black
        Assert.True(lit <= elapsed * 30 + 2, $"{lit} frames in {elapsed:0.000} s.");
    }
}
