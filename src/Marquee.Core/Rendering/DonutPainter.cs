using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Pies and donuts, in the largest square that fits the box. `track` paints the whole ring, then each slice is drawn
/// with its own style: glows first, then shadows, then fills, then strokes, so no slice's glow covers its neighbour.
static class DonutPainter
{
    /// One slice's place this frame: degrees clockwise from 3 o'clock, and its share of the total, from 0 to 1.
    readonly record struct Arc(int Index, float Start, float Sweep, double Share);

    /// Outside labels sit this many font sizes beyond the ring.
    const float LabelOffset = 0.3f;

    /// An outside label within about 15° of 12 or 6 o'clock (the cosine of 75°) is centred on its point, not aligned to
    /// a side.
    const float SideAlign = 0.26f;

    /// A stale slice's notch is its gap, but never narrower than this, so it shows even with `gap: 0`.
    const float StaleNotch = 2;

    /// `sliceStyles` is each slice's style this frame, in order: the donut's, then the slice's own, then the donut's
    /// matching `when` rules.
    public static void Paint(RenderContext ctx, DonutWidget donut, StyleSpec style, IReadOnlyList<StyleSpec> sliceStyles)
    {
        var canvas = ctx.Canvas;
        var bounds = donut.Bounds;
        using var font = donut.Labels == DonutLabels.None ? null : TextPainter.Font(ctx, style);

        // Outside labels need room above and below the ring, inside the box.
        var outer = Math.Min(bounds.Width, bounds.Height) / 2;
        if (font is not null && donut.Labels == DonutLabels.Outside)
            outer -= font.Size * LabelOffset + LineHeight(font);
        if (outer <= 0) return;
        var inner = outer * donut.Hole;
        var centre = new SKPoint(bounds.MidX, bounds.MidY);
        var area = SKRect.Create(centre.X - outer, centre.Y - outer, outer * 2, outer * 2);

        if (style.Track is { } track)
        {
            using var ring = Path(centre, outer, inner, 0, 360);
            using var paint = Paints.Create(track, area);
            canvas.DrawPath(ring, paint);
        }

        var arcs = Arcs(ctx.Sensors, donut);
        var paths = arcs.Select(a => Path(centre, outer, inner, a.Start, a.Sweep)).ToList();
        try
        {
            for (var i = 0; i < arcs.Count; i++)
                Glow(ctx, paths[i], sliceStyles[arcs[i].Index]);
            for (var i = 0; i < arcs.Count; i++)
                Shadow(canvas, paths[i], sliceStyles[arcs[i].Index]);
            for (var i = 0; i < arcs.Count; i++)
            {
                var slice = sliceStyles[arcs[i].Index];
                using var paint = Paints.Create(slice.Fill ?? new SolidPaint(slice.Color ?? StyleDefaults.Color), area);
                canvas.DrawPath(paths[i], paint);
            }

            for (var i = 0; i < arcs.Count; i++)
                Outline(canvas, paths[i], sliceStyles[arcs[i].Index], area);
        }
        finally
        {
            foreach (var path in paths) path.Dispose();
        }

        if (font is not null)
            foreach (var arc in arcs)
                Label(canvas, donut, arc, font, style, centre, outer, inner);
    }

    /// Each live slice's angle is its share of the live slices' total, after the gaps. A stale slice takes no share but
    /// keeps its gap, at least StaleNotch wide, so it shows as a notch where it would be; a slice of 0 or less is left
    /// out, gap and all. The gaps together take at most half the circle, and a slice alone fills it.
    static List<Arc> Arcs(SensorSnapshot sensors, DonutWidget donut)
    {
        var slots = new List<(int Index, double? Value)>();
        for (var i = 0; i < donut.Slices.Count; i++)
        {
            var value = donut.Slices[i].Source.Evaluate(sensors)?.Number;
            if (value is null or > 0) slots.Add((i, value));
        }

        var arcs = new List<Arc>();
        var total = slots.Sum(s => s.Value ?? 0);
        if (total <= 0) return arcs;

        // With at most 12 slots each gap's cap is 15° or more, so a notch never takes more than a gap's share.
        var gap = slots.Count > 1 ? Math.Min(donut.Gap, 180f / slots.Count) : 0;
        var notch = Math.Max(gap, StaleNotch);
        var stale = slots.Count(s => s.Value is null);
        var available = 360 - gap * (slots.Count - stale) - notch * stale;
        var angle = donut.StartAngle + gap / 2;
        foreach (var (index, slot) in slots)
        {
            if (slot is not { } value)
            {
                angle += notch;
                continue;
            }

            var share = value / total;
            var sweep = (float)(available * share);
            if (sweep > 0) arcs.Add(new Arc(index, angle, sweep, share));
            angle += sweep + gap;
        }

        return arcs;
    }

    /// A ring sector, or a pie wedge when there is no hole. A whole turn is two circles, so it has no seam.
    static SKPath Path(SKPoint centre, float outer, float inner, float start, float sweep)
    {
        var path = new SKPath();
        if (sweep >= 360)
        {
            path.AddCircle(centre.X, centre.Y, outer);
            if (inner > 0) path.AddCircle(centre.X, centre.Y, inner, SKPathDirection.CounterClockwise);
            return path;
        }

        path.ArcTo(Square(centre, outer), start, sweep, forceMoveTo: true);
        if (inner > 0) path.ArcTo(Square(centre, inner), start + sweep, -sweep, forceMoveTo: false);
        else path.LineTo(centre);
        path.Close();
        return path;
    }

    static SKRect Square(SKPoint centre, float radius) => SKRect.Create(centre.X - radius, centre.Y - radius, radius * 2, radius * 2);

    static void Glow(RenderContext ctx, SKPath path, StyleSpec style)
    {
        if (Effect.Glow(style, ctx.Time) is not { } glow) return;
        using var blur = glow.Blur();
        using var paint = glow.Paint(blur);
        ctx.Canvas.DrawPath(path, paint);
    }

    static void Shadow(SKCanvas canvas, SKPath path, StyleSpec style)
    {
        if (Effect.Shadow(style) is not { } shadow) return;
        using var blur = shadow.Blur();
        using var paint = shadow.Paint(blur);
        canvas.Save();
        canvas.Translate(shadow.Dx, shadow.Dy);
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    static void Outline(SKCanvas canvas, SKPath path, StyleSpec style, SKRect area)
    {
        if (style.Stroke is not { Paint: { } strokePaint } stroke) return;
        var width = stroke.Width ?? StyleDefaults.StrokeWidth;
        if (width <= 0) return;
        using var paint = Paints.Create(strokePaint, area, style: SKPaintStyle.Stroke);
        paint.StrokeWidth = width;
        paint.StrokeJoin = SKStrokeJoin.Round;
        canvas.DrawPath(path, paint);
    }

    /// The slice's label, then its share if the donut has a format, in the font and letter spacing a text widget with
    /// the donut's style would use. Inside, the label is centred on the ring's middle and needs the slice's arc there to
    /// be as long as the label and the whole label to lie within the ring. Outside, it sits beyond the ring, leaning
    /// away from it on the side it is on, and needs an arc a line long at that distance; it stays inside the box across,
    /// so a box with too little room beside the ring pulls it in over the ring rather than into whatever is next to it.
    static void Label(SKCanvas canvas, DonutWidget donut, Arc arc, SKFont font, StyleSpec style, SKPoint centre, float outer, float inner)
    {
        var slice = donut.Slices[arc.Index];
        var share = donut.Format is { } format ? Formatting.Apply(format, SensorValue.Of(arc.Share * 100)) : null;
        var text = string.Join(" ", new[] { slice.Label, share }.Where(t => !string.IsNullOrEmpty(t)));
        if (text.Length == 0) return;

        var spacing = style.LetterSpacing ?? StyleDefaults.LetterSpacing;
        var width = TextPainter.Width(font, text, spacing);
        var height = LineHeight(font);
        var radians = (arc.Start + arc.Sweep / 2) * MathF.PI / 180;
        var (dx, dy) = (MathF.Cos(radians), MathF.Sin(radians));
        var sweep = arc.Sweep * MathF.PI / 180;

        SKPoint middle;
        if (donut.Labels == DonutLabels.Inside)
        {
            var radius = (outer + inner) / 2;
            if (sweep * radius < width) return;
            middle = new SKPoint(centre.X + dx * radius, centre.Y + dy * radius);
            if (!InRing(SKRect.Create(middle.X - width / 2, middle.Y - height / 2, width, height), centre, outer, inner)) return;
        }
        else
        {
            var radius = outer + font.Size * LabelOffset;
            if (sweep * radius < height) return;
            // Labels on the right start at their point and those on the left end there; near 12 and 6 o'clock they
            // are centred on it. All of them sit above or below it by how far up or down the ring they are.
            var x = centre.X + dx * radius;
            x = dx > SideAlign ? x + width / 2 : dx < -SideAlign ? x - width / 2 : x;
            var bounds = donut.Bounds;
            middle = new SKPoint(
                Math.Max(bounds.Left + width / 2, Math.Min(x, bounds.Right - width / 2)),
                centre.Y + dy * radius + dy * height / 2);
        }

        var metrics = font.Metrics;
        using var paint = new SKPaint { IsAntialias = true, Color = style.Color ?? StyleDefaults.Color };
        TextPainter.DrawLine(canvas, text, middle.X - width / 2, middle.Y - (metrics.Ascent + metrics.Descent) / 2, font, spacing, paint);
    }

    /// Whether all of `box` lies on the ring: its farthest corner within the outer edge, and its nearest point outside
    /// the hole.
    static bool InRing(SKRect box, SKPoint centre, float outer, float inner)
    {
        var far = new SKPoint(Math.Max(Math.Abs(box.Left - centre.X), Math.Abs(box.Right - centre.X)),
            Math.Max(Math.Abs(box.Top - centre.Y), Math.Abs(box.Bottom - centre.Y)));
        var near = new SKPoint(Math.Max(0, Math.Max(box.Left - centre.X, centre.X - box.Right)),
            Math.Max(0, Math.Max(box.Top - centre.Y, centre.Y - box.Bottom)));
        return far.Length <= outer && near.Length >= inner;
    }

    static float LineHeight(SKFont font) => font.Metrics.Descent - font.Metrics.Ascent;
}
