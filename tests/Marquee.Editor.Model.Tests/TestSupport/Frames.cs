using SkiaSharp;

namespace Marquee.Editor.Model.Tests.TestSupport;

/// Reading the session's frames.
static class Frames
{
    public static SKColor PixelAt(SKImage image, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    /// The colour at (x, y) of a frame drawn now, at animation time 0.
    public static SKColor PixelAt(EditorSession session, int x, int y)
    {
        using var frame = session.RenderFrame(TimeSpan.Zero) ?? throw new InvalidOperationException("No theme to draw.");
        return PixelAt(frame, x, y);
    }

    public static bool Near(SKColor actual, SKColor expected, int tolerance = 8) =>
        Math.Abs(actual.Red - expected.Red) <= tolerance && Math.Abs(actual.Green - expected.Green) <= tolerance
        && Math.Abs(actual.Blue - expected.Blue) <= tolerance;

    public static void AssertNear(SKColor expected, SKColor actual) =>
        Assert.True(Near(actual, expected), $"expected {expected}, got {actual}");
}
