using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// A gauge, drawn as a dial when it has a needle or ticks: the arc (none at thickness 0), the ticks and their labels
/// inside it, then the needle over everything. All of it sits in the largest square that fits the box, as the arc does,
/// and the dial's radius is that square's half: the arc's outer edge.
///
/// The ticks and labels are printed on the dial's face, so they take no shadow or glow. The needle stands above the face,
/// so it takes the style's shadow, as text does; it takes no glow, which would bloom over the labels it passes.
static class DialPainter
{
    /// Space between the inner ends of the major ticks and their labels.
    const float LabelGap = 4;

    public static void Paint(RenderContext ctx, GaugeWidget gauge, StyleSpec style, double? value)
    {
        if (gauge.Thickness > 0)
            ShapePainters.Gauge(ctx, gauge, style, value);
        if (gauge.Ticks is { } ticks)
            Ticks(ctx, gauge, ticks, style);
        if (gauge.Needle is { } needle)
            Needle(ctx, gauge, needle, style, value);
    }

    static void Ticks(RenderContext ctx, GaugeWidget gauge, TickSpec ticks, StyleSpec style)
    {
        var (center, radius) = Geometry(gauge);
        var outer = radius - gauge.Thickness;
        var perMajor = ticks.Minor + 1;
        var steps = ticks.Major * perMajor;
        // On a full circle the last tick would land on the first, and its label on the first's.
        var full = Math.Abs(gauge.SweepAngle) >= 360;
        var last = full ? steps - 1 : steps;

        using var marks = new SKPath();
        for (var step = 0; step <= last; step++)
        {
            var angle = Angle(gauge, (float)step / steps);
            var length = step % perMajor == 0 ? ticks.Length : ticks.Length / 2;
            marks.MoveTo(At(center, angle, outer));
            marks.LineTo(At(center, angle, outer - length));
        }

        var color = ticks.Color ?? style.Color ?? StyleDefaults.Color;
        using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = ticks.Width, Color = color })
            ctx.Canvas.DrawPath(marks, paint);

        if (ticks.Labels is { } format)
            Labels(ctx, gauge, ticks, format, style, center, outer - ticks.Length - LabelGap, full);
    }

    /// Each major tick's value, centred on the point that puts its text just inside `inside`, whatever its angle, in
    /// the font and letter spacing a text widget with this style would use. A label whose text would overlap the last
    /// one drawn, or the first, is left out, so a small dial or a large font shows every other label rather than a smudge.
    static void Labels(RenderContext ctx, GaugeWidget gauge, TickSpec ticks, string format, StyleSpec style, SKPoint center,
        float inside, bool full)
    {
        using var font = TextPainter.Font(ctx, style);
        using var paint = new SKPaint { IsAntialias = true, Color = style.Color ?? StyleDefaults.Color };
        var spacing = style.LetterSpacing ?? StyleDefaults.LetterSpacing;

        SKRect? first = null, previous = null;
        var last = full ? ticks.Major - 1 : ticks.Major;
        for (var i = 0; i <= last; i++)
        {
            var fraction = (double)i / ticks.Major;
            var text = Formatting.Apply(format, SensorValue.Of(gauge.Min + (gauge.Max - gauge.Min) * fraction));
            if (text.Length == 0) continue;

            var ink = TextPainter.Ink(font, text, spacing);
            var angle = Angle(gauge, (float)fraction);
            var radians = angle * MathF.PI / 180;
            // How far the text's box reaches towards the tick, from its centre, along this angle.
            var half = MathF.Abs(MathF.Cos(radians)) * ink.Width / 2 + MathF.Abs(MathF.Sin(radians)) * ink.Height / 2;
            var middle = At(center, angle, inside - half);
            var box = SKRect.Create(middle.X - ink.Width / 2, middle.Y - ink.Height / 2, ink.Width, ink.Height);
            if (previous?.IntersectsWith(box) == true || first?.IntersectsWith(box) == true) continue;

            TextPainter.DrawLine(ctx.Canvas, text, middle.X - ink.MidX, middle.Y - ink.MidY, font, spacing, paint);
            first ??= box;
            previous = box;
        }
    }

    /// Like the arc's fill, the needle only shows a live value: while the value is missing or stale it isn't drawn, so
    /// the dial never shows a reading it doesn't have. The hub stays, as part of the face.
    static void Needle(RenderContext ctx, GaugeWidget gauge, NeedleSpec needle, StyleSpec style, double? value)
    {
        var (center, radius) = Geometry(gauge);
        using var shape = new SKPath();
        if (value is { } v)
            Pointer(shape, center, Angle(gauge, Fraction(v, gauge.Min, gauge.Max)), radius * needle.Length, needle.Width);
        // Clockwise, as the pointer is, so where they overlap the shape stays filled.
        if (needle.Cap > 0)
            shape.AddCircle(center.X, center.Y, needle.Cap, SKPathDirection.Clockwise);
        if (shape.IsEmpty) return;

        var canvas = ctx.Canvas;
        if (Effect.Shadow(style) is { } shadow)
        {
            using var blur = shadow.Blur();
            using var paint = shadow.Paint(blur);
            canvas.Save();
            canvas.Translate(shadow.Dx, shadow.Dy);
            canvas.DrawPath(shape, paint);
            canvas.Restore();
        }

        using var body = new SKPaint { IsAntialias = true, Color = needle.Color ?? style.Color ?? StyleDefaults.Color };
        canvas.DrawPath(shape, body);
    }

    /// A needle `width` wide at the centre, rounded behind it, narrowing to a point `length` away along `angle`. It runs
    /// clockwise: from the left of its base, to the tip, to the right, and round the back.
    static void Pointer(SKPath path, SKPoint center, float angle, float length, float width)
    {
        var half = width / 2;
        path.MoveTo(At(center, angle - 90, half));
        path.LineTo(At(center, angle, length));
        path.LineTo(At(center, angle + 90, half));
        path.ArcTo(SKRect.Create(center.X - half, center.Y - half, width, width), angle + 90, 180, false);
        path.Close();
    }

    static (SKPoint Center, float Radius) Geometry(GaugeWidget gauge)
    {
        var bounds = gauge.Bounds;
        return (new SKPoint(bounds.MidX, bounds.MidY), Math.Min(bounds.Width, bounds.Height) / 2);
    }

    /// Degrees clockwise from 3 o'clock at this fraction of the way along the arc.
    static float Angle(GaugeWidget gauge, float fraction) => gauge.StartAngle + gauge.SweepAngle * fraction;

    static SKPoint At(SKPoint center, float degrees, float distance)
    {
        var radians = degrees * MathF.PI / 180;
        return new SKPoint(center.X + MathF.Cos(radians) * distance, center.Y + MathF.Sin(radians) * distance);
    }

    static float Fraction(double value, double min, double max) => (float)Math.Clamp((value - min) / (max - min), 0, 1);
}
