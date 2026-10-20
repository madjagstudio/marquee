using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// The shape widget. A rect is drawn as a group's box is; an ellipse fills its box, with the stroke inside it; a line is
/// drawn by the stroke alone, centred on it; an arc is a gauge's band with no value, painted by `fill`, and its stroke
/// lies inside the band. Glow is drawn first, then shadow, then the shape, as for text; each effect is the shape's whole
/// silhouette, fill and stroke together, tinted and blurred.
static class ShapeWidgetPainter
{
    public static void Paint(RenderContext ctx, ShapeWidget shape, StyleSpec style)
    {
        if (Effect.Glow(style, ctx.Time) is { } glow)
            Silhouette(ctx, shape, style, glow);
        if (Effect.Shadow(style) is { } shadow)
            Silhouette(ctx, shape, style, shadow);
        Draw(ctx, shape, style);
    }

    /// Draws the shape on a layer that is tinted, blurred and moved by `effect` as it is composited. Tinting the
    /// composited silhouette, rather than each part, keeps a translucent effect even where fill and stroke overlap. The
    /// layer covers only the shape, so a blur costs what the shape's size does, not the canvas's.
    static void Silhouette(RenderContext ctx, ShapeWidget shape, StyleSpec style, Effect effect)
    {
        using var tint = SKColorFilter.CreateBlendMode(effect.Color, SKBlendMode.SrcIn);
        using var tinted = SKImageFilter.CreateColorFilter(tint);
        using var blurred = effect.Sigma > 0 ? SKImageFilter.CreateBlur(effect.Sigma, effect.Sigma, tinted) : null;
        using var moved = SKImageFilter.CreateOffset(effect.Dx, effect.Dy, blurred ?? tinted);
        using var paint = new SKPaint { ImageFilter = moved };

        // Wide enough for a line's stroke and caps, which reach past its box.
        var reach = (style.Stroke?.Width ?? StyleDefaults.StrokeWidth) + 1;
        ctx.Canvas.SaveLayer(SKRect.Inflate(shape.Bounds, reach, reach), paint);
        Draw(ctx, shape, style);
        ctx.Canvas.Restore();
    }

    static void Draw(RenderContext ctx, ShapeWidget shape, StyleSpec style)
    {
        switch (shape.Shape)
        {
            case ShapeKind.Rect:
                BoxPainter.Paint(ctx, shape.Bounds, style);
                break;
            case ShapeKind.Ellipse:
                Ellipse(ctx.Canvas, shape.Bounds, style);
                break;
            case ShapeKind.Line:
                Line(ctx.Canvas, shape.Bounds, style);
                break;
            default:
                Arc(ctx.Canvas, shape, style);
                break;
        }
    }

    static void Ellipse(SKCanvas canvas, SKRect bounds, StyleSpec style)
    {
        if (style.Fill is { } fill)
        {
            using var paint = Paints.Create(fill, bounds);
            canvas.DrawOval(bounds, paint);
        }

        if (Stroke(style) is { } stroke)
        {
            using var paint = Paints.Create(stroke.Paint, bounds, style: SKPaintStyle.Stroke);
            paint.StrokeWidth = stroke.Width;
            canvas.DrawOval(SKRect.Inflate(bounds, -stroke.Width / 2, -stroke.Width / 2), paint);
        }
    }

    /// From the box's top-left corner to its bottom-right. A gradient is laid across the stroke's extent, which has a
    /// size even when the box is 0 wide or high.
    static void Line(SKCanvas canvas, SKRect bounds, StyleSpec style)
    {
        if (Stroke(style) is not { } stroke) return;
        using var paint = Paints.Create(stroke.Paint, SKRect.Inflate(bounds, stroke.Width / 2, stroke.Width / 2), style: SKPaintStyle.Stroke);
        paint.StrokeWidth = stroke.Width;
        paint.StrokeCap = style.Stroke?.Cap ?? StyleDefaults.StrokeCap;
        canvas.DrawLine(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, paint);
    }

    /// Laid out as ShapePainters.Gauge lays out its arc: the band's middle runs round the largest square that fits the
    /// box, inset by half the thickness, and `stroke.cap` shapes its ends.
    static void Arc(SKCanvas canvas, ShapeWidget arc, StyleSpec style)
    {
        var (square, oval) = ShapePainters.ArcBounds(arc.Bounds, arc.Thickness);
        var cap = style.Stroke?.Cap ?? StyleDefaults.StrokeCap;

        if (style.Fill is { } fill)
        {
            using var paint = Paints.Create(fill, square, style: SKPaintStyle.Stroke);
            paint.StrokeWidth = arc.Thickness;
            paint.StrokeCap = cap;
            canvas.DrawArc(oval, arc.StartAngle, arc.SweepAngle, false, paint);
        }

        if (Stroke(style) is { } stroke)
        {
            // The band's outline, stroked twice as wide and clipped to the band, leaves the stroke's width inside it.
            using var middle = new SKPath();
            middle.AddArc(oval, arc.StartAngle, arc.SweepAngle);
            using var shaper = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = arc.Thickness, StrokeCap = cap };
            using var band = shaper.GetFillPath(middle);
            using var paint = Paints.Create(stroke.Paint, square, style: SKPaintStyle.Stroke);
            paint.StrokeWidth = stroke.Width * 2;
            canvas.Save();
            canvas.ClipPath(band, antialias: true);
            canvas.DrawPath(band, paint);
            canvas.Restore();
        }
    }

    /// The style's stroke, when it draws: a paint and a width above 0, as for a box's outline.
    static (PaintSpec Paint, float Width)? Stroke(StyleSpec style) =>
        style.Stroke is { Paint: { } paint } stroke && (stroke.Width ?? StyleDefaults.StrokeWidth) is var width and > 0
            ? (paint, width)
            : null;
}
