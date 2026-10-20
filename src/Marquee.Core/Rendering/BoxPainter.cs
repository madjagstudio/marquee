using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// A widget's box: `fill` behind, `stroke` drawn just inside the bounds, both with `radius` corners.
static class BoxPainter
{
    public static void Paint(RenderContext ctx, SKRect bounds, StyleSpec style)
    {
        Background(ctx, bounds, style);
        Outline(ctx, bounds, style);
    }

    public static void Background(RenderContext ctx, SKRect bounds, StyleSpec style)
    {
        if (style.Fill is not { } fill) return;
        using var paint = Paints.Create(fill, bounds);
        Rect(ctx.Canvas, bounds, style.Radius ?? StyleDefaults.Radius, paint);
    }

    public static void Outline(RenderContext ctx, SKRect bounds, StyleSpec style)
    {
        if (style.Stroke is not { Paint: { } strokePaint } stroke) return;
        var width = stroke.Width ?? StyleDefaults.StrokeWidth;
        if (width <= 0) return;

        using var paint = Paints.Create(strokePaint, bounds, style: SKPaintStyle.Stroke);
        paint.StrokeWidth = width;
        var inset = SKRect.Inflate(bounds, -width / 2, -width / 2);
        Rect(ctx.Canvas, inset, Math.Max(0, (style.Radius ?? StyleDefaults.Radius) - width / 2), paint);
    }

    public static void Rect(SKCanvas canvas, SKRect rect, float radius, SKPaint paint)
    {
        if (radius > 0) canvas.DrawRoundRect(rect, radius, radius, paint);
        else canvas.DrawRect(rect, paint);
    }

    /// Clips to the bounds, with rounded corners when the style has a radius. Pair with canvas.Restore().
    public static void Clip(SKCanvas canvas, SKRect bounds, StyleSpec style)
    {
        canvas.Save();
        var radius = style.Radius ?? StyleDefaults.Radius;
        if (radius > 0)
        {
            using var round = new SKRoundRect(bounds, radius);
            canvas.ClipRoundRect(round, antialias: true);
        }
        else
        {
            canvas.ClipRect(bounds);
        }
    }
}
