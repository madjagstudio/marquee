using System.Diagnostics;
using Marquee.Core.Rendering;

namespace Marquee.Core.Tests;

/// FrameTimer paces frames to about a millisecond without asking Windows for 1 ms ticks.
[Collection("Real-time render loop")]
public class FrameTimerTests
{
    static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 30);

    [Fact]
    public void Windows_has_a_high_resolution_timer()
    {
        using var timer = new FrameTimer();

        Assert.True(timer.HighResolution);
    }

    [Fact]
    public void A_frame_long_wait_lasts_a_frame()
    {
        // A plain wait rounds 33.3 ms up to the 15.6 ms tick (31 or 47 ms) unless something asked for 1 ms ticks. The
        // average says the wait is precise; a busy machine may oversleep once or twice, so single waits aren't checked.
        using var timer = new FrameTimer();
        var clock = Stopwatch.StartNew();

        for (var i = 0; i < 30; i++) Assert.True(timer.Wait(Frame, CancellationToken.None));

        Assert.InRange(clock.Elapsed.TotalMilliseconds / 30, Frame.TotalMilliseconds - 2, Frame.TotalMilliseconds + 2);
    }

    [Fact]
    public void Cancelling_ends_the_wait_at_once()
    {
        using var timer = new FrameTimer();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var clock = Stopwatch.StartNew();

        Assert.False(timer.Wait(TimeSpan.FromSeconds(10), stop.Token));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"the wait took {clock.Elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public void Nothing_to_wait_for_returns_straight_away()
    {
        using var timer = new FrameTimer();

        Assert.True(timer.Wait(TimeSpan.Zero, CancellationToken.None));
        Assert.True(timer.Wait(TimeSpan.FromMilliseconds(-5), CancellationToken.None));
        Assert.False(timer.Wait(TimeSpan.Zero, new CancellationToken(canceled: true)));
    }
}
