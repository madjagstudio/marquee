using SkiaSharp;

namespace Marquee.MakeIcon;

/// Marquee's mark: a rounded bar shaped like the panel, in the example themes' blue gradient, with a bright line
/// scrolling across it. Drawn on a 256-unit square and scaled; at the small sizes the notification area uses (16 to
/// 32 px) the bar's edges and the line are put on whole pixels, so they stay sharp instead of blurring into their
/// neighbours.
static class IconMark
{
    static readonly SKColor Cyan = SKColor.Parse("#36D1DC");
    static readonly SKColor Blue = SKColor.Parse("#5B86E5");

    /// The mark at `size` × `size` pixels, as a PNG.
    public static byte[] Png(int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        Draw(surface.Canvas, size);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    static void Draw(SKCanvas canvas, int size)
    {
        canvas.Clear(SKColors.Transparent);
        var k = size / 256f;
        var snap = size <= 32;
        float At(float units) => snap ? MathF.Round(units * k) : units * k;

        // The bar: nearly the full width, a little over half the height, like the 1920 × 480 panel made chunkier so it
        // still reads at 16 px.
        var bar = new SKRect(At(8), At(60), size - At(8), At(196));
        var radius = Math.Max(1.5f, 36 * k);
        using (var fill = new SKPaint { IsAntialias = true })
        {
            fill.Shader = SKShader.CreateLinearGradient(new SKPoint(bar.Left, bar.Top), new SKPoint(bar.Right, bar.Bottom),
                [Cyan, Blue], SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(bar, radius, radius, fill);
        }

        // The line: a long stroke and a short fainter one after it, like text scrolling off the right edge.
        var thickness = Math.Max(1f, At(24));
        var middle = snap ? MathF.Floor((bar.Top + bar.Bottom - thickness) / 2) + thickness / 2 : (bar.Top + bar.Bottom) / 2;
        using var line = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            StrokeCap = SKStrokeCap.Round,
            Color = SKColors.White,
        };
        var cap = thickness / 2;
        canvas.DrawLine(At(44) + cap, middle, At(156) - cap, middle, line);
        line.Color = SKColors.White.WithAlpha(140);
        canvas.DrawLine(At(180) + cap, middle, At(212) - cap, middle, line);
    }
}
