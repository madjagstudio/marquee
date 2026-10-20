namespace Marquee.App.Tests;

public class BalloonLimiterTests
{
    DateTime _now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    BalloonLimiter Limiter() => new(TimeSpan.FromMinutes(1), () => _now);

    [Fact]
    public void One_balloon_of_a_kind_per_interval()
    {
        var limiter = Limiter();

        Assert.True(limiter.TryTake("theme"));
        Assert.False(limiter.TryTake("theme"));

        _now += TimeSpan.FromSeconds(59);
        Assert.False(limiter.TryTake("theme"));

        _now += TimeSpan.FromSeconds(1);
        Assert.True(limiter.TryTake("theme"));
        Assert.False(limiter.TryTake("theme"));
    }

    [Fact]
    public void One_kind_never_holds_back_another()
    {
        var limiter = Limiter();

        Assert.True(limiter.TryTake("config"));
        Assert.True(limiter.TryTake("theme"));
        Assert.True(limiter.TryTake("handover"));
        Assert.False(limiter.TryTake("config"));
    }
}
