using Marquee.Core.Devices;

namespace Marquee.Core.Tests;

public class OrientationTests
{
    [Theory]
    [InlineData(Orientation.Portrait, 480, 1920)]
    [InlineData(Orientation.PortraitFlipped, 480, 1920)]
    [InlineData(Orientation.Landscape, 1920, 480)]
    [InlineData(Orientation.LandscapeFlipped, 1920, 480)]
    public void CanvasSize(Orientation orientation, int width, int height)
    {
        var size = orientation.CanvasSize();

        Assert.Equal(width, size.Width);
        Assert.Equal(height, size.Height);
    }

    [Theory]
    [InlineData(Orientation.Portrait, true)]
    [InlineData(Orientation.PortraitFlipped, true)]
    [InlineData(Orientation.Landscape, false)]
    [InlineData(Orientation.LandscapeFlipped, false)]
    public void IsPortrait(Orientation orientation, bool expected) =>
        Assert.Equal(expected, orientation.IsPortrait());
}
