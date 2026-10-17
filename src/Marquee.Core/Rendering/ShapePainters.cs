using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Bars, gauges and graphs: `track` paints the empty part, `fill` paints the value (default: the style's color),
/// and gradients span the whole widget so the color shown tracks the value.
static class ShapePainters
{
    public static void Bar(RenderContext ctx, BarWidget bar, StyleSpec style, double? value)
    {
        var canvas = ctx.Canvas;
        var bounds = bar.Bounds;
        if (style.Track is { } track)
        {
            using var paint = Paints.Create(track, bounds);
            BoxPainter.Rect(canvas, bounds, style.Radius ?? 0, paint);
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
        var bounds = gauge.Bounds;
        var size = Math.Min(bounds.Width, bounds.Height);
        var square = SKRect.Create(bounds.MidX - size / 2, bounds.MidY - size / 2, size, size);
        var oval = SKRect.Inflate(square, -gauge.Thickness / 2, -gauge.Thickness / 2);
        var cap = style.Stroke?.Cap ?? SKStrokeCap.Butt;

        if (style.Track is { } track)
            Arc(ctx.Canvas, oval, gauge.StartAngle, gauge.SweepAngle, track, square, gauge.Thickness, cap);
        if (value is { } v && Fraction(v, gauge.Min, gauge.Max) is var fraction and > 0)
            Arc(ctx.Canvas, oval, gauge.StartAngle, gauge.SweepAngle * fraction, ValuePaint(style), square, gauge.Thickness, cap);
    }

    /// Newest sample at the right edge, one step per sample, so a filling history grows in from the right.
    public static void Graph(RenderContext ctx, GraphWidget graph, StyleSpec style)
    {
        var canvas = ctx.Canvas;
        var bounds = graph.Bounds;
        if (style.Track is { } track)
        {
            using var paint = Paints.Create(track, bounds);
            BoxPainter.Rect(canvas, bounds, style.Radius ?? 0, paint);
        }

        var history = ctx.Sensors.History(graph.Key);
        var count = Math.Min(history.Count, graph.Points);
        if (count < 2) return;

        var first = history.Count - count;
        var (min, max) = (double.MaxValue, double.MinValue);
        for (var i = first; i < history.Count; i++)
            (min, max) = (Math.Min(min, history[i]), Math.Max(max, history[i]));
        min = graph.Min ?? min;
        max = graph.Max ?? max;
        // An explicit bound stays authoritative: padding a degenerate or inverted range must not move it.
        if (max - min < 1e-9)
            (min, max) = (graph.Min, graph.Max) switch
            {
                (null, null) => (min - 1, max + 1),
                (null, { } hi) => (hi - 1, hi),
                ({ } lo, _) => (lo, lo + 1),
            };

        var step = bounds.Width / (graph.Points - 1);
        SKPoint Point(int i) => new(
            bounds.Right - (history.Count - 1 - i) * step,
            bounds.Bottom - (float)((Math.Clamp(history[i], min, max) - min) / (max - min)) * bounds.Height);

        using var line = new SKPath();
        line.MoveTo(Point(first));
        for (var i = first + 1; i < history.Count; i++)
            line.LineTo(Point(i));

        BoxPainter.Clip(canvas, bounds, style);
        if (style.Fill is { } fill)
        {
            using var area = new SKPath(line);
            area.LineTo(bounds.Right, bounds.Bottom);
            area.LineTo(Point(first).X, bounds.Bottom);
            area.Close();
            using var paint = Paints.Create(fill, bounds);
            canvas.DrawPath(area, paint);
        }

        var stroke = style.Stroke;
        if ((stroke?.Width ?? 2) is var width and > 0)
        {
            using var paint = Paints.Create(stroke?.Paint ?? new SolidPaint(style.Color ?? StyleDefaults.Color), bounds, style: SKPaintStyle.Stroke);
            paint.StrokeWidth = width;
            paint.StrokeCap = stroke?.Cap ?? SKStrokeCap.Round;
            paint.StrokeJoin = SKStrokeJoin.Round;
            canvas.DrawPath(line, paint);
        }

        canvas.Restore();
    }

    static float Fraction(double value, double min, double max) => (float)Math.Clamp((value - min) / (max - min), 0, 1);

    static PaintSpec ValuePaint(StyleSpec style) => style.Fill ?? new SolidPaint(style.Color ?? StyleDefaults.Color);

    static void Arc(SKCanvas canvas, SKRect oval, float start, float sweep, PaintSpec spec, SKRect area, float thickness, SKStrokeCap cap)
    {
        using var paint = Paints.Create(spec, area, style: SKPaintStyle.Stroke);
        paint.StrokeWidth = thickness;
        paint.StrokeCap = cap;
        canvas.DrawArc(oval, start, sweep, false, paint);
    }
}
