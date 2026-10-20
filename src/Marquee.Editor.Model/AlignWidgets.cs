using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

public enum Alignment { Left, Center, Right, Top, Middle, Bottom }

public enum Distribution { Horizontal, Vertical }

/// Lines up two or more widgets on an edge or centre line of the box round them all, as drawn on the canvas (turned
/// widgets by the upright box round them), as one PlaceWidgets step. Each moves on the one axis, its `at` written in its
/// own group's coordinates and rounded to whole pixels. Locked widgets, and those using an anchor or alias, stay where
/// they are, and a widget inside another being aligned goes with it; with fewer than two left, nothing changes.
public sealed record AlignWidgets(IReadOnlyList<YamlPath> Widgets, Alignment How) : EditorCommand
{
    public override string Label => How switch
    {
        Alignment.Left => "Align left",
        Alignment.Center => "Align centre",
        Alignment.Right => "Align right",
        Alignment.Top => "Align top",
        Alignment.Middle => "Align middle",
        _ => "Align bottom",
    };

    public override CommandResult Run(CommandContext context) =>
        Arrange.Run(context, Widgets, Label, boxes =>
        {
            var all = boxes.Aggregate(SKRect.Union);
            return boxes.Select(box => How switch
            {
                Alignment.Left => new SKPoint(all.Left - box.Left, 0),
                Alignment.Center => new SKPoint(all.MidX - box.MidX, 0),
                Alignment.Right => new SKPoint(all.Right - box.Right, 0),
                Alignment.Top => new SKPoint(0, all.Top - box.Top),
                Alignment.Middle => new SKPoint(0, all.MidY - box.MidY),
                _ => new SKPoint(0, all.Bottom - box.Bottom),
            }).ToList();
        });
}

/// Spaces three or more widgets evenly across or down: the first and last, by their centres, stay where they are, and
/// the ones between move so the gaps between the boxes (as drawn) are equal. One PlaceWidgets step, left out and
/// rounded as AlignWidgets says; two widgets have no gap to even, so nothing changes.
public sealed record DistributeWidgets(IReadOnlyList<YamlPath> Widgets, Distribution Axis) : EditorCommand
{
    public override string Label => Axis == Distribution.Horizontal ? "Distribute horizontally" : "Distribute vertically";

    public override CommandResult Run(CommandContext context) =>
        Arrange.Run(context, Widgets, Label, boxes =>
        {
            var across = Axis == Distribution.Horizontal;
            Func<SKRect, float> start = across ? b => b.Left : b => b.Top;
            Func<SKRect, float> length = across ? b => b.Width : b => b.Height;
            var order = Enumerable.Range(0, boxes.Count).OrderBy(i => start(boxes[i]) + length(boxes[i]) / 2).ToList();
            var moves = new SKPoint[boxes.Count];
            if (boxes.Count < 3) return moves;

            var (first, last) = (boxes[order[0]], boxes[order[^1]]);
            var between = order.Skip(1).SkipLast(1).Sum(i => length(boxes[i]));
            var gap = (start(last) - (start(first) + length(first)) - between) / (boxes.Count - 1);
            var at = start(first) + length(first) + gap;
            foreach (var i in order.Skip(1).SkipLast(1))
            {
                var move = at - start(boxes[i]);
                moves[i] = across ? new SKPoint(move, 0) : new SKPoint(0, move);
                at += length(boxes[i]) + gap;
            }

            return moves;
        });
}

/// What aligning, distributing and nudging share: choosing the widgets that can move, and moving each by a distance on
/// the canvas, written in its own coordinates.
static class Arrange
{
    /// The widgets of `paths` that can move: there, unlocked (with their groups) and editable, and not inside another
    /// of them, in the order given.
    public static List<WidgetRef> Movable(CanvasGeometry geometry, IReadOnlyList<YamlPath> paths)
    {
        var movable = paths.Where(path => geometry.Widget(path) is { IsEditable: true } && !geometry.IsLocked(path));
        return LayerEdits.Outermost(movable).Select(path => geometry.Widget(path)!).ToList();
    }

    /// Moves two or more of `paths`' movable widgets by the canvas distances `moves` gives for their boxes as drawn, as
    /// one PlaceWidgets step labelled `label`; with fewer than two, the document as it was.
    public static CommandResult Run(CommandContext context, IReadOnlyList<YamlPath> paths, string label,
        Func<IReadOnlyList<SKRect>, IReadOnlyList<SKPoint>> moves)
    {
        var geometry = CanvasGeometry.Of(context);
        var widgets = Movable(geometry, paths);
        if (widgets.Count < 2) return context.Document;

        var by = moves(widgets.Select(w => geometry.BoundsOf(w.Path)).ToList());
        var placements = widgets.Select((widget, i) =>
        {
            var local = CanvasGeometry.Inverse(geometry.ParentFrame(widget.Path)).MapVector(by[i].X, by[i].Y);
            return new WidgetPlacement(widget.Path, CanvasMath.Moved(widget.LocalBounds, local), widget.Rotate);
        });
        return new PlaceWidgets(placements.ToList()) { Description = label }.Run(context);
    }
}
