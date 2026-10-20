using Marquee.Host.SensorFeed;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

public class ConnectionRateTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Up_to_the_limit_needs_no_wait()
    {
        var time = new ManualTime(T0);
        var rate = new ConnectionRate(time, 5, TimeSpan.FromSeconds(1));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(TimeSpan.Zero, rate.Wait());
            rate.Record();
        }
    }

    [Fact]
    public void Past_the_limit_the_next_waits_until_the_oldest_is_a_window_old()
    {
        var time = new ManualTime(T0);
        var rate = new ConnectionRate(time, 5, TimeSpan.FromSeconds(1));
        for (var i = 0; i < 5; i++)
        {
            rate.Record();
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(TimeSpan.FromMilliseconds(500), rate.Wait());

        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(TimeSpan.Zero, rate.Wait());
        rate.Record();
        Assert.Equal(TimeSpan.FromMilliseconds(100), rate.Wait());
    }

    [Fact]
    public async Task WaitTurn_returns_once_the_clock_has_moved_on()
    {
        var time = new ManualTime(T0);
        var rate = new ConnectionRate(time, 1, TimeSpan.FromSeconds(1));
        rate.Record();

        var turn = rate.WaitTurn(CancellationToken.None);
        Assert.False(turn.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));
        await turn.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.Zero, rate.Wait());
    }
}
