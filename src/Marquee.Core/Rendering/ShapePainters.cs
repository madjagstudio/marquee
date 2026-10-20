using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Bars and gauges: `track` paints the empty part, `fill` paints the value (default: the style's color), and
/// gradients span the whole widget so the color shown tracks the value. GraphPainter draws graphs the same way.
static class ShapePainters
{
    public static void Bar(RenderContext ctx, BarWidget bar, StyleSpec style, double? value)
    {
        if (bar.Segments is { } segments)
        {
            SegmentPainter.Bar(ctx.Canvas, bar, segments, style, value);
            BoxPainter.Outline(ctx, bar.Bounds, style);
            return;
        }

        var canvas = ctx.Canvas;
        var bounds = bar.Bounds;
        if (style.Track is { } track)
        {
            using var paint = Paints.Create(track, bounds);
            BoxPainter.Rect(canvas, bounds, style.Radius ?? StyleDefaults.Radius, paint);
        }

        if (value is { } v && Fraction(v, bar.Min, bar.Max) is var fraction and > 0)
        {
            var filled = bar.Direction == BarDirection.Horizontal
                ? new SKRect(bounds.Left, bounds.Top, bounds.Left + bounds.Width * fraction, bounds.Bottom)
                : new SKRect(bounds.Left, bounds.Bottom - bounds.Height * fraction, bounds.Right, bounds.Bottom);
            BoxPainter.Clip(canvas, bounds, style);
            using var paint = Paints.Create(ValuePaint(style), bounds);
            canvas.DrawRect(filled, paint);
            canvas.Restore();
        }

        BoxPainter.Outline(ctx, bounds, style);
    }

    public static void Gauge(RenderContext ctx, GaugeWidget gauge, StyleSpec style, double? value)
    {
        var (square, oval) = ArcBounds(gauge.Bounds, gauge.Thickness);
        var cap = style.Stroke?.Cap ?? StyleDefaults.StrokeCap;

        if (gauge.Segments is { } segments)
        {
            SegmentPainter.Gauge(ctx.Canvas, gauge, segments, style, value, square);
        }
        else
        {
            if (style.Track is { } track)
                Arc(ctx.Canvas, oval, gauge.StartAngle, gauge.SweepAngle, track, square, gauge.Thickness, cap);
            if (value is { } v && Fraction(v, gauge.Min, gauge.Max) is var fraction and > 0)
                Arc(ctx.Canvas, oval, gauge.StartAngle, gauge.SweepAngle * fraction, ValuePaint(style), square, gauge.Thickness, cap);
        }
    }

    /// How far `value` is along the scale from `min` to `max`, from 0 to 1.
    public static float Fraction(double value, double min, double max) => (float)Math.Clamp((value - min) / (max - min), 0, 1);

    public static PaintSpec ValuePaint(StyleSpec style) => style.Fill ?? new SolidPaint(style.Color ?? StyleDefaults.Color);

    static void Arc(SKCanvas canvas, SKRect oval, float start, float sweep, PaintSpec spec, SKRect area, float thickness, SKStrokeCap cap)
    {
        using var paint = Paints.Create(spec, area, style: SKPaintStyle.Stroke);
        paint.StrokeWidth = thickness;
        paint.StrokeCap = cap;
        canvas.DrawArc(oval, start, sweep, false, paint);
    }

    /// The largest square that fits `bounds`, centred in it, and the oval inside it that a band `thickness` wide runs
    /// along: where a gauge's arc and an arc shape lie.
    public static (SKRect Square, SKRect Oval) ArcBounds(SKRect bounds, float thickness)
    {
        var size = Math.Min(bounds.Width, bounds.Height);
        var square = SKRect.Create(bounds.MidX - size / 2, bounds.MidY - size / 2, size, size);
        return (square, SKRect.Inflate(square, -thickness / 2, -thickness / 2));
    }
}
