using System.Drawing;
using SkiaSharp;

namespace Marquee.Preview.Tests;

public class FrameBitmapTests
{
    static SKImage Frame(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Black);
        using var red = new SKPaint { Color = SKColors.Red };
        using var blue = new SKPaint { Color = SKColors.Blue };
        surface.Canvas.DrawRect(0, 0, 100, 100, red);
        surface.Canvas.DrawRect(width - 100, height - 100, 100, 100, blue);
        return surface.Snapshot();
    }

    [Theory]
    [InlineData(1920, 480)]
    [InlineData(480, 1920)]
    public void Copies_size_and_pixels_without_swapping_channels(int width, int height)
    {
        using var frame = Frame(width, height);

        using var bitmap = FrameBitmap.ToBitmap(frame);

        Assert.Equal(new Size(width, height), bitmap.Size);
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), bitmap.GetPixel(10, 10));
        Assert.Equal(Color.FromArgb(255, 0, 0, 255), bitmap.GetPixel(width - 10, height - 10));
        Assert.Equal(Color.FromArgb(255, 0, 0, 0), bitmap.GetPixel(width / 2, height / 2));
    }

    [Fact]
    public void The_bitmap_outlives_the_frame()
    {
        Bitmap bitmap;
        using (var frame = Frame(200, 200))
            bitmap = FrameBitmap.ToBitmap(frame);

        using (bitmap)
            Assert.Equal(Color.FromArgb(255, 255, 0, 0), bitmap.GetPixel(10, 10));
    }
}
