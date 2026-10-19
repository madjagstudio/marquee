using Marquee.Providers.Fps;

namespace Marquee.Providers.Fps.Tests;

public class FrameStatsTests
{
    const int Game = 4242;
    const string Main = "0x1000";
    const string Overlay = "0x2000";
    const double Tolerance = 1e-6;

    /// An arbitrary point on the provider's monotonic clock where the tests start.
    static readonly TimeSpan T0 = TimeSpan.FromMinutes(5);

    static TimeSpan At(double ms) => T0 + TimeSpan.FromTicks((long)Math.Round(ms * TimeSpan.TicksPerMillisecond));

    /// Adds frames at a steady rate on one swap chain, starting one interval after fromMs, each arriving when it
    /// starts. Returns the last frame's time.
    static double Steady(FrameStats stats, double fromMs, double seconds, double fps, int pid = Game, string chain = Main,
        bool withStartTime = true)
    {
        var interval = 1000 / fps;
        var count = (int)Math.Round(seconds * fps);
        var t = fromMs;
        for (var i = 1; i <= count; i++)
        {
            t = fromMs + i * interval;
            stats.Add(new PresentMonFrame("game.exe", pid, chain, withStartTime ? t : null, interval), At(t));
        }
        return t;
    }

    static FpsReading Read(FrameStats stats, int pid = Game)
    {
        Assert.True(stats.TryRead(pid, out var reading));
        return reading;
    }

    [Fact]
    public void A_steady_60_fps_reads_60()
    {
        var stats = new FrameStats();
        Steady(stats, 0, seconds: 10, fps: 60);

        var reading = Read(stats);

        Assert.Equal(60, reading.Fps, Tolerance);
        Assert.Equal(1000.0 / 60, reading.FrameTimeMs, Tolerance);
        Assert.Equal(60, reading.OnePercentLowFps, Tolerance);
    }

    [Fact]
    public void A_steady_144_fps_reads_144()
    {
        var stats = new FrameStats();
        Steady(stats, 0, seconds: 3, fps: 144);

        var reading = Read(stats);

        Assert.Equal(144, reading.Fps, Tolerance);
        Assert.Equal(1000.0 / 144, reading.FrameTimeMs, Tolerance);
        Assert.Equal(144, reading.OnePercentLowFps, Tolerance);
    }

    [Fact]
    public void Exactly_one_second_of_frames_reads_the_true_rate()
    {
        // 60 frames span 59 gaps between their start times; counting each frame's own interval covers the whole second.
        var stats = new FrameStats();
        Steady(stats, 0, seconds: 1, fps: 60);

        Assert.Equal(60, Read(stats).Fps, Tolerance);
    }

    [Fact]
    public void Stutters_lower_the_one_percent_low()
    {
        var stats = new FrameStats();
        var intervals = new List<double>();
        for (var i = 0; i < 500; i++)
            intervals.Add(i is 100 or 200 or 300 or 400 ? 50 : 16.7);
        var t = 0.0;
        foreach (var interval in intervals)
        {
            t += interval;
            stats.Add(new PresentMonFrame("game.exe", Game, Main, t, interval), At(t));
        }

        var reading = Read(stats);

        // The slowest 1% of 500 frames is 5 frames: the four 50 ms stutters and one ordinary 16.7 ms frame.
        var slowest = intervals.OrderByDescending(ms => ms).Take(intervals.Count / 100).ToList();
        Assert.Equal([50, 50, 50, 50, 16.7], slowest);
        Assert.Equal(1000 / slowest.Average(), reading.OnePercentLowFps, Tolerance);
        Assert.Equal(1000 / 16.7, reading.Fps, Tolerance);
    }

    [Fact]
    public void The_one_percent_low_needs_at_least_one_frame()
    {
        var stats = new FrameStats();
        stats.Add(new PresentMonFrame("game.exe", Game, Main, 0, 20), At(0));
        stats.Add(new PresentMonFrame("game.exe", Game, Main, 40, 40), At(40));

        Assert.Equal(25, Read(stats).OnePercentLowFps, Tolerance);
    }

    [Fact]
    public void The_one_percent_low_forgets_frames_older_than_ten_seconds()
    {
        var stats = new FrameStats();
        var t = Steady(stats, 0, seconds: 5, fps: 10);
        Steady(stats, t, seconds: 11, fps: 60);

        Assert.Equal(60, Read(stats).OnePercentLowFps, Tolerance);
    }

    [Fact]
    public void The_busiest_swap_chain_wins_over_a_slower_overlay()
    {
        var stats = new FrameStats();
        Steady(stats, 0, seconds: 2, fps: 10, chain: Overlay);
        Steady(stats, 0, seconds: 2, fps: 60, chain: Main);

        var reading = Read(stats);

        Assert.Equal(60, reading.Fps, Tolerance);
        Assert.Equal(1000.0 / 60, reading.FrameTimeMs, Tolerance);
        Assert.Equal(60, reading.OnePercentLowFps, Tolerance);
    }

    [Fact]
    public void A_swap_chain_that_stopped_presenting_gives_way_to_one_that_is_still_presenting()
    {
        var stats = new FrameStats();
        Steady(stats, 0, seconds: 2, fps: 60, chain: Main);
        Steady(stats, 0, seconds: 5, fps: 30, chain: Overlay);

        Assert.Equal(30, Read(stats).Fps, Tolerance);
    }

    [Fact]
    public void A_process_without_frames_has_no_reading()
    {
        var stats = new FrameStats();
        Assert.False(stats.TryRead(Game, out _));

        Steady(stats, 0, seconds: 1, fps: 60, pid: 7);
        Assert.False(stats.TryRead(Game, out _));
    }

    [Fact]
    public void Frames_are_placed_by_their_start_time_when_they_have_one()
    {
        // Every frame arrives at once (a burst read from the pipe), so only the start times can say which frames fall
        // in the last second: 30 fps for two seconds, then 60 fps.
        var stats = new FrameStats();
        var t = 0.0;
        void Add(double interval)
        {
            t += interval;
            stats.Add(new PresentMonFrame("game.exe", Game, Main, t, interval), At(0));
        }
        for (var i = 0; i < 60; i++) Add(1000.0 / 30);
        for (var i = 0; i < 90; i++) Add(1000.0 / 60);

        Assert.Equal(60, Read(stats).Fps, Tolerance);
    }

    [Fact]
    public void Frames_without_a_start_time_are_placed_by_when_they_arrived()
    {
        var stats = new FrameStats();
        var t = Steady(stats, 0, seconds: 2, fps: 30, withStartTime: false);
        Steady(stats, t, seconds: 1.5, fps: 60, withStartTime: false);

        Assert.Equal(60, Read(stats).Fps, Tolerance);
    }

    [Fact]
    public void The_reading_says_when_the_newest_frame_arrived()
    {
        var stats = new FrameStats();
        var t = Steady(stats, 0, seconds: 1, fps: 60);

        Assert.Equal(At(t), Read(stats).LastArrival);
    }

    [Fact]
    public void Pruning_drops_a_process_that_stopped_presenting()
    {
        var stats = new FrameStats();
        Steady(stats, 0, seconds: 1, fps: 60, pid: 1);

        stats.Prune(At(9_000));
        Assert.True(stats.TryRead(1, out _));

        stats.Prune(At(11_500));
        Assert.False(stats.TryRead(1, out _));
    }

    [Fact]
    public void Adding_frames_also_prunes_quiet_processes()
    {
        var stats = new FrameStats();
        Steady(stats, 0, seconds: 1, fps: 60, pid: 1);
        Steady(stats, 11_000, seconds: 1, fps: 60, pid: 2);

        Assert.False(stats.TryRead(1, out _));
        Assert.True(stats.TryRead(2, out _));
    }

    [Fact]
    public void A_very_fast_swap_chain_keeps_a_bounded_number_of_frames()
    {
        var stats = new FrameStats();
        Steady(stats, 0, seconds: 5, fps: 1000);

        Assert.Equal(FrameStats.MaxFramesPerSwapChain, stats.FramesKept(Game));
        Assert.Equal(1000, Read(stats).Fps, 1e-3);
    }

    [Fact]
    public async Task Reading_while_frames_are_added_is_safe()
    {
        var stats = new FrameStats();
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 20; i++)
                Steady(stats, i * 2000.0, seconds: 2, fps: 1000, pid: 1 + i % 3);
        });

        while (!writer.IsCompleted)
        {
            if (stats.TryRead(1, out var reading)) Assert.True(reading.Fps > 0);
            stats.Prune(At(0));
        }

        await writer;
        Assert.True(stats.TryRead(1, out _));
    }
}
