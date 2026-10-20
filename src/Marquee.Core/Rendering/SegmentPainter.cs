using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Bars and gauges cut into segments, like a VU meter. `fill` paints the lit segments and `track` the unlit ones, and
/// each paint is laid across the whole bar or ring, so a gradient runs through the segments as it runs through a
/// continuous fill. `radius` rounds every segment's corners.
static class SegmentPainter
{
    /// How many of `count` segments a value lights. A segment lights once the value reaches its middle, so the lit
    /// length is the whole number of segments nearest the value: never more than half a segment off a continuous fill,
    /// none at min and all at max. A NaN lights none.
    public static int Lit(double value, double min, double max, int count)
    {
        // Multiplying before dividing keeps the thresholds exact for round ranges: 15 of 100 in 10 segments is 1.5.
        var lit = Math.Floor((value - min) * count / (max - min) + 0.5);
        return lit >= count ? count : lit > 0 ? (int)lit : 0;
    }

    public static void Bar(SKCanvas canvas, BarWidget bar, SegmentSpec segments, StyleSpec style, double? value)
    {
        var bounds = bar.Bounds;
        var horizontal = bar.Direction == BarDirection.Horizontal;
        var length = horizontal ? bounds.Width : bounds.Height;
        var pitch = (length + segments.Gap) / segments.Count;
        var lit = value is { } v ? Lit(v, bar.Min, bar.Max, segments.Count) : 0;
        var radius = style.Radius ?? StyleDefaults.Radius;

        using var on = Paints.Create(ShapePainters.ValuePaint(style), bounds);
        using var off = style.Track is { } track ? Paints.Create(track, bounds) : null;
        for (var i = 0; i < segments.Count; i++)
        {
            if ((i < lit ? on : off) is not { } paint) continue;
            // Edges on whole pixels, measured from the bar's start, so whole-pixel gaps stay crisp and even; the
            // segments then differ by at most a pixel.
            var from = Whole(i * pitch);
            var to = Whole(i * pitch + pitch - segments.Gap);
            var rect = horizontal
                ? new SKRect(bounds.Left + from, bounds.Top, bounds.Left + to, bounds.Bottom)
                : new SKRect(bounds.Left, bounds.Bottom - to, bounds.Right, bounds.Bottom - from);
            BoxPainter.Rect(canvas, rect, radius, paint);
        }
    }

    /// Segments run from the start in the sweep's direction. A full ring starts half a gap in, so its join is a gap
    /// like the others.
    public static void Gauge(SKCanvas canvas, GaugeWidget gauge, SegmentSpec segments, StyleSpec style, double? value, SKRect square)
    {
        var sweep = Math.Abs(gauge.SweepAngle);
        var ring = sweep >= 360;
        var gaps = ring ? segments.Count : segments.Count - 1;
        var length = (sweep - gaps * segments.Gap) / segments.Count;
        var direction = Math.Sign(gauge.SweepAngle);
        var offset = ring ? segments.Gap / 2 : 0;
        var lit = value is { } v ? Lit(v, gauge.Min, gauge.Max, segments.Count) : 0;

        var outer = square.Width / 2;
        var inner = Math.Max(0, outer - gauge.Thickness);
        var corner = style.Radius ?? StyleDefaults.Radius;
        var centre = new SKPoint(square.MidX, square.MidY);

        using var on = Paints.Create(ShapePainters.ValuePaint(style), square);
        using var off = style.Track is { } track ? Paints.Create(track, square) : null;
        for (var i = 0; i < segments.Count; i++)
        {
            if ((i < lit ? on : off) is not { } paint) continue;
            var from = gauge.StartAngle + direction * (offset + i * (length + segments.Gap));
            var to = from + direction * length;
            using var path = Sector(centre, outer, inner, Math.Min(from, to), length, corner);
            canvas.DrawPath(path, paint);
        }
    }

    static float Whole(float offset) => MathF.Floor(offset + 0.5f);

    /// The part of the ring between `outer` and `inner` radii from `start`, clockwise for `sweep` degrees, with corners
    /// rounded to `corner` px: each corner is a circle touching the straight side and the arc. A corner can't be more
    /// than half the ring's thickness, or wider than the segment, so it shrinks to fit; at the centre (an inner radius
    /// of 0) the inner corners stay sharp.
    static SKPath Sector(SKPoint centre, float outer, float inner, float start, float sweep, float corner)
    {
        var end = start + sweep;
        var half = MathF.Sin(MathF.Min(sweep, 180) / 2 * MathF.PI / 180);
        var thickness = outer - inner;
        var outerCorner = Math.Clamp(corner, 0, Math.Min(thickness / 2, outer * half / (1 + half)));
        var innerCorner = half >= 1 ? Math.Clamp(corner, 0, thickness / 2) : Math.Clamp(corner, 0, Math.Min(thickness / 2, inner * half / (1 - half)));

        // How far round the arc each corner's circle reaches, in degrees.
        var outerTurn = Degrees(MathF.Asin(outerCorner / (outer - outerCorner)));
        var innerTurn = inner + innerCorner > 0 ? Degrees(MathF.Asin(innerCorner / (inner + innerCorner))) : 0;

        var path = new SKPath();
        path.MoveTo(Polar(centre, (outer - outerCorner) * Cos(outerTurn), start));
        Corner(path, Polar(centre, outer - outerCorner, start + outerTurn), outerCorner, start - 90, 90 + outerTurn);
        path.ArcTo(Circle(centre, outer), start + outerTurn, sweep - 2 * outerTurn, false);
        Corner(path, Polar(centre, outer - outerCorner, end - outerTurn), outerCorner, end - outerTurn, 90 + outerTurn);
        if (inner > 0)
        {
            path.LineTo(Polar(centre, (inner + innerCorner) * Cos(innerTurn), end));
            Corner(path, Polar(centre, inner + innerCorner, end - innerTurn), innerCorner, end + 90, 90 - innerTurn);
            path.ArcTo(Circle(centre, inner), end - innerTurn, -(sweep - 2 * innerTurn), false);
            Corner(path, Polar(centre, inner + innerCorner, start + innerTurn), innerCorner, start + innerTurn + 180, 90 - innerTurn);
        }
        else
        {
            path.LineTo(centre);
        }

        path.Close();
        return path;
    }

    /// A rounded corner: part of the circle of `radius` about `centre`, or the corner point itself when `radius` is 0.
    static void Corner(SKPath path, SKPoint centre, float radius, float start, float sweep)
    {
        if (radius > 0) path.ArcTo(Circle(centre, radius), start, sweep, false);
        else path.LineTo(centre);
    }

    static SKRect Circle(SKPoint centre, float radius) => new(centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius);

    static SKPoint Polar(SKPoint centre, float radius, float degrees) =>
        new(centre.X + radius * Cos(degrees), centre.Y + radius * MathF.Sin(degrees * MathF.PI / 180));

    static float Cos(float degrees) => MathF.Cos(degrees * MathF.PI / 180);

    static float Degrees(float radians) => radians * 180 / MathF.PI;
}
