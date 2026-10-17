using SkiaSharp;

namespace Marquee.Core.Tests.TestSupport;

static class Images
{
    /// 1920×480 black canvas with a 100×100 marker block at the top-left corner.
    public static SKImage Landscape(SKColor marker)
    {
        using var surface = SKSurface.Create(new SKImageInfo(1920, 480));
        surface.Canvas.Clear(SKColors.Black);
        using var paint = new SKPaint { Color = marker };
        surface.Canvas.DrawRect(0, 0, 100, 100, paint);
        return surface.Snapshot();
    }

    /// 480×1920 random pixels: compresses badly, so JPEG size tracks quality.
    public static SKImage Noise(int seed)
    {
        var random = new Random(seed);
        using var bitmap = new SKBitmap(480, 1920);
        bitmap.Pixels = Enumerable.Range(0, 480 * 1920)
            .Select(_ => new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)))
            .ToArray();
        return SKImage.FromBitmap(bitmap);
    }

    public static SKColor PixelAt(SKImage image, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }
}
