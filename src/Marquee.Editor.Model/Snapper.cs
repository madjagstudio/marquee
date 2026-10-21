using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// The lines of a dragged box that may snap: a move snaps all six, a resize only the edges it moves.
[Flags]
public enum SnapEdges
{
    None = 0,
    Left = 1,
    CenterX = 2,
    Right = 4,
    Top = 8,
    CenterY = 16,
    Bottom = 32,
    All = Left | CenterX | Right | Top | CenterY | Bottom,
}

/// A magenta guide line to draw while a snap holds, from one end to the other, in theme pixels.
public readonly record struct SnapGuide(SKPoint From, SKPoint To);

/// How far to move the dragged box to snap it (Offset, theme pixels; zero on an axis that didn't snap), and the guides
/// to draw.
public sealed record SnapResult(SKPoint Offset, IReadOnlyList<SnapGuide> Guides)
{
    public static SnapResult None { get; } = new(SKPoint.Empty, []);
}

/// Snaps a dragged box, in canvas pixels, to the canvas's edges and centre lines and to the edges and centres of the
/// other visible widgets (each box as drawn, turned: CanvasGeometry.BoundsOf), at any depth in groups. The dragged
/// widgets, and what is inside them, are left out. Each axis snaps on its own, to the nearest line within Threshold
/// screen pixels, so the reach in theme pixels grows as the canvas zooms out. A multi-selection snaps by the box round
/// it all. Alt held down places freely: the drag doesn't call Snap then (CanvasDrag).
public sealed class Snapper
{
    /// How near, in screen pixels, a line must come to snap.
    public const float Threshold = 6;

    readonly List<Line> _vertical = [];
    readonly List<Line> _horizontal = [];

    /// Snaps to everything visible on `geometry`'s canvas but `moving` and what they hold.
    public Snapper(CanvasGeometry geometry, IEnumerable<YamlPath> moving)
    {
        var size = geometry.CanvasSize;
        AddBox(new SKRect(0, 0, size.Width, size.Height));
        var skipped = moving.ToList();
        foreach (var widget in All(geometry.WidgetsIn(null)))
        {
            if (geometry.IsHidden(widget.Path) || skipped.Any(path => Within(widget.Path, path))) continue;
            AddBox(geometry.BoundsOf(widget.Path));
        }
    }

    /// The offset that snaps `box`'s `edges`, and the guides for the box once snapped: for a move (both of an axis's
    /// edges) the whole box moves; for a resize, only the edge.
    public SnapResult Snap(SKRect box, float zoom, SnapEdges edges = SnapEdges.All)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoom);
        var reach = Threshold / zoom;
        var dx = Nearest(XLines(box, edges), _vertical, reach);
        var dy = Nearest(YLines(box, edges), _horizontal, reach);
        if (dx is null && dy is null) return SnapResult.None;

        var snapped = Moved(box, edges, dx ?? 0, dy ?? 0);
        var guides = new List<SnapGuide>();
        if (dx is not null)
        {
            foreach (var x in XLines(snapped, edges).Distinct())
                if (Span(_vertical, x, snapped.Top, snapped.Bottom) is { } span)
                    guides.Add(new SnapGuide(new SKPoint(x, span.From), new SKPoint(x, span.To)));
        }

        if (dy is not null)
        {
            foreach (var y in YLines(snapped, edges).Distinct())
                if (Span(_horizontal, y, snapped.Left, snapped.Right) is { } span)
                    guides.Add(new SnapGuide(new SKPoint(span.From, y), new SKPoint(span.To, y)));
        }

        return new SnapResult(new SKPoint(dx ?? 0, dy ?? 0), guides);
    }

    /// The box with its snapped edges moved: the whole box on an axis where both its edges snap, else the one edge.
    static SKRect Moved(SKRect box, SnapEdges edges, float dx, float dy)
    {
        if (Both(edges, SnapEdges.Left, SnapEdges.Right)) (box.Left, box.Right) = (box.Left + dx, box.Right + dx);
        else if (edges.HasFlag(SnapEdges.Left)) box.Left += dx;
        else if (edges.HasFlag(SnapEdges.Right)) box.Right += dx;

        if (Both(edges, SnapEdges.Top, SnapEdges.Bottom)) (box.Top, box.Bottom) = (box.Top + dy, box.Bottom + dy);
        else if (edges.HasFlag(SnapEdges.Top)) box.Top += dy;
        else if (edges.HasFlag(SnapEdges.Bottom)) box.Bottom += dy;
        return box;
    }

    static bool Both(SnapEdges edges, SnapEdges a, SnapEdges b) => edges.HasFlag(a) && edges.HasFlag(b);

    static IEnumerable<float> XLines(SKRect box, SnapEdges edges)
    {
        if (edges.HasFlag(SnapEdges.Left)) yield return box.Left;
        if (edges.HasFlag(SnapEdges.CenterX)) yield return box.MidX;
        if (edges.HasFlag(SnapEdges.Right)) yield return box.Right;
    }

    static IEnumerable<float> YLines(SKRect box, SnapEdges edges)
    {
        if (edges.HasFlag(SnapEdges.Top)) yield return box.Top;
        if (edges.HasFlag(SnapEdges.CenterY)) yield return box.MidY;
        if (edges.HasFlag(SnapEdges.Bottom)) yield return box.Bottom;
    }

    /// The smallest move, within `reach`, that puts one of `lines` on a target; null when none is that near. Themes are
    /// written in whole pixels, so only a whole move counts: a line that a fraction would put on its target (a 101 px
    /// box's centre on the canvas's, at 960) can't be written there, and isn't snapped to rather than shown snapped and
    /// written half a pixel off.
    static float? Nearest(IEnumerable<float> lines, List<Line> targets, float reach)
    {
        const float Whole = 0.01f;
        float? best = null;
        foreach (var line in lines)
        {
            foreach (var target in targets)
            {
                var move = MathF.Round(target.At - line);
                if (Math.Abs(target.At - line - move) > Whole) continue;
                if (Math.Abs(move) <= reach && (best is null || Math.Abs(move) < Math.Abs(best.Value))) best = move;
            }
        }

        return best;
    }

    /// Where a guide along `at` runs: from the dragged box's `from`–`to` across every target on that line; null when no
    /// target is on it.
    static (float From, float To)? Span(List<Line> targets, float at, float from, float to)
    {
        const float Same = 0.01f;
        var on = targets.Where(target => Math.Abs(target.At - at) <= Same).ToList();
        if (on.Count == 0) return null;
        return (Math.Min(from, on.Min(target => target.From)), Math.Max(to, on.Max(target => target.To)));
    }

    void AddBox(SKRect box)
    {
        foreach (var x in new[] { box.Left, box.MidX, box.Right }) _vertical.Add(new Line(x, box.Top, box.Bottom));
        foreach (var y in new[] { box.Top, box.MidY, box.Bottom }) _horizontal.Add(new Line(y, box.Left, box.Right));
    }

    static IEnumerable<WidgetRef> All(IEnumerable<WidgetRef> widgets) =>
        widgets.SelectMany(widget => All(widget.Children).Prepend(widget));

    /// True when `path` is `widget` or inside it.
    static bool Within(YamlPath path, YamlPath widget) =>
        path.Segments.Count >= widget.Segments.Count && path.Segments.Take(widget.Segments.Count).SequenceEqual(widget.Segments);

    /// A target line at `At` (an x for a vertical line, a y for a horizontal one), running from `From` to `To` along it.
    readonly record struct Line(float At, float From, float To);
}
