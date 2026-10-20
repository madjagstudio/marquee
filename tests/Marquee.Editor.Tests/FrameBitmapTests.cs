using System.Windows;
using System.Windows.Media;
using Marquee.Editor.Canvas;
using Marquee.Editor.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Tests;

/// Frames into the canvas's WriteableBitmap: one copy, byte for byte.
public class FrameBitmapTests
{
    [Fact]
    public void The_bitmap_holds_the_frames_pixels_exactly() => Sta.Run(() =>
    {
        using var image = Image(7, 5, seed: 1);

        var bitmap = FrameBitmap.Copy(image, null);

        Assert.Equal(PixelFormats.Pbgra32, bitmap.Format);
        Assert.Equal((7, 5), (bitmap.PixelWidth, bitmap.PixelHeight));
        var expected = Bgra(image);
        var actual = new byte[7 * 5 * 4];
        bitmap.CopyPixels(new Int32Rect(0, 0, 7, 5), actual, 7 * 4, 0);
        Assert.Equal(expected, actual);
    });

    [Fact]
    public void A_frame_of_the_same_size_goes_into_the_same_bitmap() => Sta.Run(() =>
    {
        using var first = Image(7, 5, seed: 1);
        using var second = Image(7, 5, seed: 2);
        using var larger = Image(9, 5, seed: 3);
        var bitmap = FrameBitmap.Copy(first, null);

        Assert.Same(bitmap, FrameBitmap.Copy(second, bitmap));
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(3, 2, 1, 1), pixel, 4, 0);
        Assert.Equal(Bgra(second).AsSpan((2 * 7 + 3) * 4, 4).ToArray(), pixel);

        var resized = FrameBitmap.Copy(larger, bitmap);
        Assert.NotSame(bitmap, resized);
        Assert.Equal(9, resized.PixelWidth);
    });

    /// A small frame of every kind of pixel: opaque, translucent (premultiplied) and clear.
    static SKImage Image(int width, int height, int seed)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var random = new Random(seed);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var alpha = (byte)(((x + y) % 3) switch { 0 => 255, 1 => random.Next(1, 255), _ => 0 });
            bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha));
        }

        return SKImage.FromBitmap(bitmap);
    }

    /// The frame's pixels as Skia reads them out, BGRA premultiplied, row after row.
    static byte[] Bgra(SKImage image)
    {
        using var pixels = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Assert.True(image.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0));
        return pixels.Bytes;
    }
}
