using Marquee.Core.Rendering;

namespace Marquee.Core.Tests;

public class SmootherTests
{
    static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void First_value_snaps() => Assert.Equal(40, new Smoother().Next(40, Ms(0), 500));

    [Fact]
    public void Eases_out_towards_a_new_value()
    {
        var smoother = new Smoother();
        smoother.Next(0, Ms(0), 500);

        Assert.Equal(0, smoother.Next(100, Ms(1000), 500));
        Assert.Equal(87.5, smoother.Next(100, Ms(1250), 500), 6);
        Assert.Equal(100, smoother.Next(100, Ms(1500), 500));
        Assert.Equal(100, smoother.Next(100, Ms(9000), 500));
    }

    [Fact]
    public void A_new_target_mid_way_starts_from_the_current_value()
    {
        var smoother = new Smoother();
        smoother.Next(0, Ms(0), 500);
        smoother.Next(100, Ms(0), 500);
        var midway = smoother.Next(100, Ms(250), 500);

        var turned = smoother.Next(0, Ms(250), 500);

        Assert.Equal(midway, turned);
        Assert.Equal(midway * 0.125, smoother.Next(0, Ms(500), 500), 6);
    }

    [Fact]
    public void Zero_duration_snaps()
    {
        var smoother = new Smoother();
        smoother.Next(0, Ms(0), 0);

        Assert.Equal(100, smoother.Next(100, Ms(1), 0));
    }
}
