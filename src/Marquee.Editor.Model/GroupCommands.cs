using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Groups the widgets at `Widgets` (Ctrl+G), which must be in one list: they become the `children` of a new group,
/// in the order the file has them, where the first of them was (WrapInGroup). The group's `at` and `size` are the
/// box round them as drawn, in whole pixels, so nothing is clipped, and each child's `at` counts from its corner, so
/// nothing moves on screen. A widget inside another being grouped goes with it. The new group becomes the selection.
public sealed record GroupWidgets(IReadOnlyList<YamlPath> Widgets) : EditorCommand
{
    public override string Label => "Group";

    public override CommandResult Run(CommandContext context)
    {
        var widgets = LayerEdits.Outermost(Widgets).Select(context.RequireWidget).ToList();
        if (widgets.Count == 0) throw new ArgumentException("Choose the widgets to group.", nameof(Widgets));
        var parent = CanvasGeometry.GroupOf(widgets[0].Path);
        if (widgets.Any(w => CanvasGeometry.GroupOf(w.Path) != parent))
            throw new ArgumentException("Only widgets in the same list can be grouped.", nameof(Widgets));

        var drawn = widgets.Select(w => SelectionFrame.Turned(w).MapRect(w.LocalBounds)).Aggregate(SKRect.Union);
        // A group's size must be at least 1 each way, where a line's box has no height (or width).
        var (left, top) = (MathF.Floor(drawn.Left), MathF.Floor(drawn.Top));
        var box = new SKRect(
            left, top, MathF.Max(MathF.Ceiling(drawn.Right), left + 1), MathF.Max(MathF.Ceiling(drawn.Bottom), top + 1));
        var header = YamlMap.Empty
            .With("type", "group")
            .With("at", new YamlList([YamlValue.Number(box.Left), YamlValue.Number(box.Top)]))
            .With("size", new YamlList([YamlValue.Number(box.Width), YamlValue.Number(box.Height)]));

        var list = LayerEdits.ListOf(parent);
        var indexes = widgets.Select(w => w.Path.Last!.Value.Index).ToList();
        var document = context.Document.WrapInGroup(list, indexes, YamlWriter.Block(header), out var moved);
        for (var i = 0; i < widgets.Count; i++)
        {
            var bounds = widgets[i].LocalBounds;
            document = PlaceWidgets.Place(document, moved[i], bounds, widgets[i].Rotate,
                SKRect.Create(bounds.Left - box.Left, bounds.Top - box.Top, bounds.Width, bounds.Height), widgets[i].Rotate);
        }

        return new CommandResult(document, [list.Item(indexes.Min())]);
    }
}

/// Ungroups the groups at `Groups` (Ctrl+Shift+G): each one's children take its place in its list (UnwrapGroup), with
/// their `at` (and, from a turned group, `rotate`) rewritten so nothing moves on screen; the group's own keys, such as
/// its style, go. Groups inside one another can be ungrouped together. Paths that aren't groups are passed over. The
/// children become the selection.
public sealed record UngroupWidgets(IReadOnlyList<YamlPath> Groups) : EditorCommand
{
    public override string Label => "Ungroup";

    public override CommandResult Run(CommandContext context)
    {
        var geometry = CanvasGeometry.Of(context);
        var groups = Groups.Distinct().Select(context.RequireWidget).Where(w => w.IsGroup).Select(w => w.Path).ToList();
        if (groups.Count == 0) return context.Document;

        // Each child of each group, and where it is in the file as the groups are unwrapped one by one: last in the file
        // first, so a group inside another is unwrapped before it and no group's own path changes before its turn.
        var children = groups.SelectMany(path => geometry.Widget(path)!.Children)
            .Select(child => (Widget: child, Path: child.Path))
            .ToList();
        var document = context.Document;
        foreach (var group in groups.OrderByDescending(path => path, FileOrder.Instance))
        {
            document = document.UnwrapGroup(group, out var moved);
            var (list, index, inside) = (group.Parent!, group.Last!.Value.Index, group.Child("children"));
            for (var i = 0; i < children.Count; i++)
            {
                var path = children[i].Path;
                children[i] = children[i] with
                {
                    Path = LayerEdits.Through(path, inside, out var k)
                        ? LayerEdits.Replaced(path, inside.Segments.Count + 1, list.Item(index + k))
                        : LayerEdits.Shifted(path, list, index + 1, moved.Count - 1),
                };
            }
        }

        var kept = children.Where(child => !groups.Contains(child.Widget.Path)).ToList();
        foreach (var (widget, path) in kept)
        {
            // Into the nearest group around it that stays.
            var into = CanvasGeometry.GroupOf(widget.Path);
            while (into is not null && groups.Contains(into)) into = CanvasGeometry.GroupOf(into);
            var (bounds, rotate) = LayerEdits.Rebased(geometry, widget, into);
            document = PlaceWidgets.Place(document, path, widget.LocalBounds, widget.Rotate, bounds, rotate);
        }

        return new CommandResult(document, kept.Select(child => child.Path).OrderBy(path => path, FileOrder.Instance).ToList());
    }
}
