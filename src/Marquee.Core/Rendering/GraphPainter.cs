using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Graphs: `track` paints the background, then the gridlines, then each series back to front, all on one vertical
/// scale. A series' style is layered over the graph's: its stroke draws a line, and its fill (default: the color) the
/// area under it or its columns. The newest sample sits at the right edge, so a filling history grows in from the right.
static class GraphPainter
{
    /// The space between neighbouring columns, or groups of columns side by side, as a fraction of a sample's slot.
    const float ColumnGap = 0.2f;

    public static void Paint(RenderContext ctx, GraphWidget graph, StyleSpec style)
    {
        var canvas = ctx.Canvas;
        var bounds = graph.Bounds;
        if (style.Track is { } track)
        {
            using var paint = Paints.Create(track, bounds);
            BoxPainter.Rect(canvas, bounds, style.Radius ?? StyleDefaults.Radius, paint);
        }

        var samples = new Samples[graph.Series.Count];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = Recent(ctx.Sensors.History(graph.Series[i].Key), graph.Points, graph.Kind);
        var scale = Scale(graph, samples);
        if (graph.Grid is null && scale is null) return;

        BoxPainter.Clip(canvas, bounds, style);
        if (graph.Grid is { } grid)
            Grid(canvas, bounds, grid);
        if (scale is { } fit)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                if (samples[i].Count == 0) continue;
                var series = graph.Series[i];
                var own = ReferenceEquals(series.Style, StyleSpec.Empty) ? style : StyleSpec.Merge(style, series.Style);
                if (graph.Kind == GraphKind.Columns)
                    Columns(canvas, bounds, samples[i], fit, graph.Points, i, samples.Length, own);
                else
                    Line(canvas, bounds, samples[i], fit, graph.Points, own, area: graph.Kind == GraphKind.Area);
            }
        }

        canvas.Restore();
    }

    /// The last Count samples of one history, oldest first; Count is 0 when there are too few to draw.
    readonly record struct Samples(IReadOnlyList<double> History, int Count)
    {
        public double this[int i] => History[History.Count - Count + i];
    }

    /// A line needs two samples; a column stands on its own.
    static Samples Recent(IReadOnlyList<double> history, int points, GraphKind kind)
    {
        var count = Math.Min(history.Count, points);
        return new Samples(history, count < (kind == GraphKind.Columns ? 1 : 2) ? 0 : count);
    }

    /// The fixed bounds, with any left out fitted to every series' samples; null when there is nothing to draw.
    static (double Min, double Max)? Scale(GraphWidget graph, Samples[] samples)
    {
        var (min, max) = (double.MaxValue, double.MinValue);
        foreach (var series in samples)
            for (var i = 0; i < series.Count; i++)
                (min, max) = (Math.Min(min, series[i]), Math.Max(max, series[i]));
        if (min > max) return null;

        // Columns stand on the bottom of the scale, so a fitted scale would leave the smallest value no column at all,
        // which reads as a missing sample. Theirs takes in 0 instead.
        if (graph.Kind == GraphKind.Columns)
            (min, max) = (Math.Min(min, 0), Math.Max(max, 0));
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
        return (min, max);
    }

    static float Y(SKRect bounds, (double Min, double Max) scale, double value) =>
        bounds.Bottom - (float)((Math.Clamp(value, scale.Min, scale.Max) - scale.Min) / (scale.Max - scale.Min)) * bounds.Height;

    /// Gridlines split the height into even bands. Their edges sit on whole pixels, so a thin line stays crisp rather
    /// than smeared across two rows.
    static void Grid(SKCanvas canvas, SKRect bounds, GraphGrid grid)
    {
        if (grid.Width <= 0) return;
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = grid.Color, StrokeWidth = grid.Width };
        for (var i = 1; i <= grid.Lines; i++)
        {
            var y = MathF.Round(bounds.Top + bounds.Height * i / (grid.Lines + 1) - grid.Width / 2) + grid.Width / 2;
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, paint);
        }
    }

    /// One step per sample across the width. A line fills the area under it only when its style sets a fill; an area
    /// always does.
    static void Line(SKCanvas canvas, SKRect bounds, Samples samples, (double Min, double Max) scale, int points, StyleSpec style,
        bool area)
    {
        var step = bounds.Width / (points - 1);
        SKPoint Point(int i) => new(bounds.Right - (samples.Count - 1 - i) * step, Y(bounds, scale, samples[i]));

        using var line = new SKPath();
        line.MoveTo(Point(0));
        for (var i = 1; i < samples.Count; i++)
            line.LineTo(Point(i));

        if ((area ? ShapePainters.ValuePaint(style) : style.Fill) is { } fill)
        {
            using var under = new SKPath(line);
            under.LineTo(bounds.Right, bounds.Bottom);
            under.LineTo(Point(0).X, bounds.Bottom);
            under.Close();
            using var paint = Paints.Create(fill, bounds);
            canvas.DrawPath(under, paint);
        }

        var stroke = style.Stroke;
        if ((stroke?.Width ?? StyleDefaults.GraphStrokeWidth) is var width and > 0)
        {
            using var paint = Paints.Create(stroke?.Paint ?? new SolidPaint(style.Color ?? StyleDefaults.Color), bounds, style: SKPaintStyle.Stroke);
            paint.StrokeWidth = width;
            paint.StrokeCap = stroke?.Cap ?? StyleDefaults.GraphStrokeCap;
            paint.StrokeJoin = SKStrokeJoin.Round;
            canvas.DrawPath(line, paint);
        }
    }

    /// One slot per sample across the width, less a gap. Several series stand side by side in each slot, series
    /// `index` of `count` from the left, so none hides another however their values compare.
    ///
    /// Each column is a rectangle of its own with its edges on whole pixels, so columns stay crisp, and a column
    /// narrower than a pixel is drawn a pixel wide rather than not at all. Drawn as one antialiased path instead,
    /// thousands of columns under a pixel wide took Skia a third of a second a frame.
    static void Columns(SKCanvas canvas, SKRect bounds, Samples samples, (double Min, double Max) scale, int points, int index,
        int count, StyleSpec style)
    {
        var slot = bounds.Width / points;
        var width = slot * (1 - ColumnGap) / count;
        var bottom = Whole(bounds.Bottom);
        using var paint = Paints.Create(ShapePainters.ValuePaint(style), bounds);
        for (var i = 0; i < samples.Count; i++)
        {
            var left = bounds.Right - (samples.Count - i) * slot + slot * ColumnGap / 2 + index * width;
            var top = Whole(Y(bounds, scale, samples[i]));
            if (top >= bottom) continue;
            var x = Whole(left);
            canvas.DrawRect(new SKRect(x, top, Math.Max(Whole(left + width), x + 1), bottom), paint);
        }
    }

    /// The nearest whole pixel, halves rounding up as SegmentPainter's do.
    static float Whole(float value) => MathF.Floor(value + 0.5f);
}
