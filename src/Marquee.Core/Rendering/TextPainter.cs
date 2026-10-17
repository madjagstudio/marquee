using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Text and value widgets: the box, then one line of text aligned inside the padding and centered vertically,
/// drawn glow first, then shadow, then the text itself.
static class TextPainter
{
    public static void Paint(RenderContext ctx, SKRect bounds, StyleSpec style, string text)
    {
        BoxPainter.Paint(ctx, bounds, style);
        if (text.Length == 0) return;

        var weight = style.Weight ?? StyleDefaults.Weight;
        var typeface = ctx.Assets.Typeface(style.Font, weight);
        using var font = new SKFont(typeface, style.Size ?? StyleDefaults.Size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.None,
            Embolden = weight >= 600 && typeface.FontWeight < 600,
        };

        var padding = style.Padding ?? 0;
        var content = SKRect.Inflate(bounds, -padding, -padding);
        var width = font.MeasureText(text);
        var x = (style.Align ?? TextAlign.Left) switch
        {
            TextAlign.Center => content.MidX - width / 2,
            TextAlign.Right => content.Right - width,
            _ => content.Left,
        };
        var metrics = font.Metrics;
        var y = content.MidY - (metrics.Ascent + metrics.Descent) / 2;
        var canvas = ctx.Canvas;

        if (style.Glow is { Color: { } glowColor } && StyleEvaluator.GlowRadius(style, ctx.Time) is var radius and > 0)
        {
            using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, radius / 2);
            using var glow = new SKPaint { IsAntialias = true, Color = glowColor, MaskFilter = blur };
            canvas.DrawText(text, x, y, font, glow);
        }

        if (style.Shadow is { Color: { } shadowColor } shadow)
        {
            var sigma = (shadow.Blur ?? 0) / 2;
            using var blur = sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null;
            using var paint = new SKPaint { IsAntialias = true, Color = shadowColor, MaskFilter = blur };
            canvas.DrawText(text, x + (shadow.X ?? 2), y + (shadow.Y ?? 2), font, paint);
        }

        using var textPaint = new SKPaint { IsAntialias = true, Color = style.Color ?? StyleDefaults.Color };
        canvas.DrawText(text, x, y, font, textPaint);
    }
}
