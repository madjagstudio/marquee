using Marquee.Editor.Canvas;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

/// When the canvas draws: once soon after a request, at the theme's fps only while the theme animates, and never while
/// the window is minimised.
public class RedrawSchedulerTests
{
    /// Timestamps that move only when the test says, so a request is never held back for the 60 fps cap.
    sealed class Ticks : TimeProvider
    {
        public long Now { get; set; } = TimeSpan.TicksPerHour;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Now;

        public void Advance() => Now += TimeSpan.TicksPerSecond;
    }

    [Fact]
    public void A_request_draws_once_and_none_draws_while_inactive() => Sta.Run(() =>
    {
        var time = new Ticks();
        var draws = 0;
        using var scheduler = new RedrawScheduler(() => draws++, () => false, time);

        scheduler.Request();
        scheduler.Request();
        Sta.Pump();
        Assert.Equal(1, draws);

        scheduler.SetActive(false);
        time.Advance();
        scheduler.Request();
        Sta.Pump();
        Assert.False(scheduler.IsActive);
        Assert.Equal(1, draws);

        // Shown again, it draws a frame at once.
        time.Advance();
        scheduler.SetActive(true);
        Sta.Pump();
        Assert.Equal(2, draws);
    });

    [Fact]
    public void It_draws_at_the_themes_fps_only_while_the_theme_animates_and_never_while_inactive() => Sta.Run(() =>
    {
        var time = new Ticks();
        var animates = false;
        using var scheduler = new RedrawScheduler(() => { }, () => animates, time) { Fps = 24 };
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 24), scheduler.FrameInterval);

        scheduler.UpdateAnimation();
        Assert.False(scheduler.IsAnimating);

        animates = true;
        scheduler.UpdateAnimation();
        Assert.True(scheduler.IsAnimating);

        // Minimised, it stops, and asking again doesn't start it.
        scheduler.SetActive(false);
        Assert.False(scheduler.IsAnimating);
        scheduler.UpdateAnimation();
        Assert.False(scheduler.IsAnimating);

        // Shown again, the frame it draws at once asks whether the theme still animates.
        time.Advance();
        scheduler.SetActive(true);
        Sta.Pump();
        Assert.True(scheduler.IsAnimating);

        // After a frame, a theme that has stopped moving stops the drawing.
        animates = false;
        time.Advance();
        scheduler.Request();
        Sta.Pump();
        Assert.False(scheduler.IsAnimating);

        scheduler.Fps = 500;
        Assert.Equal(TimeSpan.FromSeconds(1.0 / RedrawScheduler.MaxFramesPerSecond), scheduler.FrameInterval);
    });
}
