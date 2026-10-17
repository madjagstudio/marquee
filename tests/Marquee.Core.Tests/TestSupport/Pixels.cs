using SkiaSharp;

namespace Marquee.Core.Tests.TestSupport;

static class Pixels
{
    public static bool Near(SKColor actual, SKColor expected, int tolerance = 8) =>
        Math.Abs(actual.Red - expected.Red) <= tolerance
        && Math.Abs(actual.Green - expected.Green) <= tolerance
        && Math.Abs(actual.Blue - expected.Blue) <= tolerance;

    public static void AssertNear(SKColor expected, SKColor actual, int tolerance = 8) =>
        Assert.True(Near(actual, expected, tolerance), $"Expected about {expected}, got {actual}");

    /// Text pixels: anything close to white.
    public static bool IsBright(SKColor c) => c.Red > 180 && c.Green > 180 && c.Blue > 180;

    public static int Count(SKImage image, SKRectI region, Func<SKColor, bool> predicate)
    {
        using var bitmap = SKBitmap.FromImage(image);
        var count = 0;
        for (var y = region.Top; y < region.Bottom; y++)
            for (var x = region.Left; x < region.Right; x++)
                if (predicate(bitmap.GetPixel(x, y)))
                    count++;
        return count;
    }
}
