using Marquee.Core.Devices;
using Marquee.Core.Devices.UniversalScreen88;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class FrameEncoderTests
{
    static bool IsRed(SKColor c) => c.Red > 200 && c.Green < 50 && c.Blue < 50;
    static bool IsBlack(SKColor c) => c.Red < 30 && c.Green < 30 && c.Blue < 30;

    [Theory]
    [InlineData(Orientation.Portrait)]
    [InlineData(Orientation.PortraitFlipped)]
    [InlineData(Orientation.Landscape)]
    [InlineData(Orientation.LandscapeFlipped)]
    public void ToPanel_produces_portrait_480x1920(Orientation orientation)
    {
        using var canvas = orientation.IsPortrait() ? Images.Portrait(SKColors.Red) : Images.Landscape(SKColors.Red);

        using var panel = FrameEncoder.ToPanel(canvas, orientation);

        Assert.Equal(480, panel.Width);
        Assert.Equal(1920, panel.Height);
    }

    [Fact]
    public void Landscape_moves_canvas_top_left_to_panel_top_right()
    {
        using var panel = FrameEncoder.ToPanel(Images.Landscape(SKColors.Red), Orientation.Landscape);

        Assert.True(IsRed(Images.PixelAt(panel, 430, 50)));
        Assert.True(IsBlack(Images.PixelAt(panel, 50, 50)));
    }

    [Fact]
    public void LandscapeFlipped_moves_canvas_top_left_to_panel_bottom_left()
    {
        using var panel = FrameEncoder.ToPanel(Images.Landscape(SKColors.Red), Orientation.LandscapeFlipped);

        Assert.True(IsRed(Images.PixelAt(panel, 50, 1870)));
        Assert.True(IsBlack(Images.PixelAt(panel, 430, 50)));
    }

    [Fact]
    public void Portrait_passes_canvas_through_unrotated()
    {
        using var panel = FrameEncoder.ToPanel(Images.Portrait(SKColors.Red), Orientation.Portrait);

        Assert.True(IsRed(Images.PixelAt(panel, 50, 50)));
        Assert.True(IsBlack(Images.PixelAt(panel, 430, 1870)));
    }

    [Fact]
    public void PortraitFlipped_moves_canvas_top_left_to_panel_bottom_right()
    {
        using var panel = FrameEncoder.ToPanel(Images.Portrait(SKColors.Red), Orientation.PortraitFlipped);

        Assert.True(IsRed(Images.PixelAt(panel, 430, 1870)));
        Assert.True(IsBlack(Images.PixelAt(panel, 50, 50)));
    }

    [Fact]
    public void EncodeJpeg_produces_decodable_portrait_jpeg()
    {
        using var panel = FrameEncoder.ToPanel(Images.Landscape(SKColors.Red), Orientation.Landscape);

        var jpeg = FrameEncoder.EncodeJpeg(panel);

        Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpeg[..2]);
        using var decoded = SKImage.FromEncodedData(jpeg);
        Assert.Equal(480, decoded.Width);
        Assert.Equal(1920, decoded.Height);
    }

    [Fact]
    public void EncodeJpeg_lowers_quality_until_it_fits()
    {
        using var noise = Images.Noise(seed: 42);
        var unconstrained = FrameEncoder.EncodeJpeg(noise, maxBytes: int.MaxValue).Length;

        var fitted = FrameEncoder.EncodeJpeg(noise, maxBytes: unconstrained - 1);

        Assert.True(fitted.Length <= unconstrained - 1);
    }

    [Fact]
    public void EncodeJpeg_throws_when_nothing_fits()
    {
        using var noise = Images.Noise(seed: 42);

        Assert.Throws<InvalidOperationException>(() => FrameEncoder.EncodeJpeg(noise, maxBytes: 100));
    }

    [Fact]
    public void Blank_is_a_full_panel_of_one_color()
    {
        using var blank = FrameEncoder.Blank(SKColors.Black);

        Assert.Equal(480, blank.Width);
        Assert.Equal(1920, blank.Height);
        Assert.True(IsBlack(Images.PixelAt(blank, 240, 960)));
    }
}
