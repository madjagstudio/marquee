using Marquee.Core.Themes;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A video frame is scaled once, to exactly the canvas pixels it covers, so frames waiting to be shown are never larger
/// than the canvas and drawing one is a plain copy.
public class FrameScalerTests
{
    static readonly SKRect Canvas = SKRect.Create(1920, 480);

    [Theory]
    [InlineData(ImageFit.Cover, 0, 0, 1920, 480)]
    [InlineData(ImageFit.Stretch, 0, 0, 1920, 480)]
    [InlineData(ImageFit.Contain, 720, 0, 1200, 480)]
    [InlineData(ImageFit.None, 760, 40, 1160, 440)]
    public void The_destination_follows_the_fit(ImageFit fit, int left, int top, int right, int bottom)
    {
        var scaler = new FrameScaler(fit, new SKSizeI(400, 400), Canvas);

        Assert.Equal(new SKRectI(left, top, right, bottom), scaler.Destination);
    }

    [Fact]
    public void A_clip_larger_than_the_canvas_at_natural_size_keeps_only_what_shows()
    {
        var scaler = new FrameScaler(ImageFit.None, new SKSizeI(3840, 2160), Canvas);

        Assert.Equal(SKRectI.Create(1920, 480), scaler.Destination);
    }

    [Fact]
    public void A_scaled_frame_is_the_destinations_size_and_shows_the_right_part_of_the_clip()
    {
        // Left half red, right half blue; at natural size on a smaller canvas, the middle of the clip shows.
        using var bitmap = new SKBitmap(new SKImageInfo(3840, 960, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 1920, 960, red);
        }
        using var pixels = bitmap.PeekPixels();
        var scaler = new FrameScaler(ImageFit.None, new SKSizeI(3840, 960), Canvas);

        using var buffers = new FrameBuffers(scaler);
        using var frame = buffers.Scale(pixels);

        Assert.Equal((1920, 480), (frame.Width, frame.Height));
        using var scaled = frame.PeekPixels();
        Assert.Equal(SKColors.Red, scaled.GetPixelColor(900, 240));
        Assert.Equal(SKColors.Blue, scaled.GetPixelColor(1020, 240));
    }
}
