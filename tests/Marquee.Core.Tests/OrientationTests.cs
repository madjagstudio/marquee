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

    [Theory]
    [InlineData("portrait", Orientation.Portrait)]
    [InlineData("portrait-flipped", Orientation.PortraitFlipped)]
    [InlineData("landscape", Orientation.Landscape)]
    [InlineData("landscape-flipped", Orientation.LandscapeFlipped)]
    public void Names_round_trip(string name, Orientation orientation)
    {
        Assert.True(OrientationNames.TryParse(name, out var parsed));
        Assert.Equal(orientation, parsed);
        Assert.Equal(name, orientation.Name());
    }

    [Fact]
    public void Every_orientation_has_a_name() =>
        Assert.Equal(Enum.GetValues<Orientation>(), OrientationNames.All.Select(o => o.Value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Landscape")]
    [InlineData("sideways")]
    public void Other_words_are_not_orientations(string? name) =>
        Assert.False(OrientationNames.TryParse(name, out _));
}
