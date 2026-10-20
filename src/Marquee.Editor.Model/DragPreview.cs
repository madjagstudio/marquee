using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// One drag's widgets and where it has put them, drawn without loading the theme again: the loaded theme's widgets with
/// the dragged ones copied with their new box and turn (and the groups holding them copied with the new children),
/// each copy standing in for its original, so a clip keeps playing and smoothing carries on (ThemeRenderer.Render).
sealed class DragPreview
{
    readonly Dictionary<YamlPath, (WidgetRef Widget, WidgetPlacement Placement)> _placed = [];

    /// The widgets to draw and what each copy stands in for, worked out again after each change.
    (IReadOnlyList<WidgetSpec> Widgets, IReadOnlyDictionary<WidgetSpec, WidgetSpec> StandIns)? _frame;

    /// Changes where the drag has `widget`: from where the file has it, the first time.
    public void Place(WidgetRef widget, Func<WidgetPlacement, WidgetPlacement> change)
    {
        var placement = _placed.TryGetValue(widget.Path, out var placed)
            ? placed.Placement
            : new WidgetPlacement(widget.Path, widget.LocalBounds, widget.Rotate);
        _placed[widget.Path] = (widget, change(placement));
        _frame = null;
    }

    /// The widgets the drag has moved from where the file has them, in the order the drag first took them.
    public IReadOnlyList<WidgetPlacement> Placements =>
        _placed.Values.Where(p => p.Placement.Bounds != p.Widget.LocalBounds || p.Placement.Rotate != p.Widget.Rotate)
            .Select(p => p.Placement).ToList();

    /// The undo step's label: what the drag did, the turn and then the size counting over a move.
    public string Description
    {
        get
        {
            var placed = _placed.Values;
            if (placed.Any(p => p.Placement.Rotate != p.Widget.Rotate)) return "Rotate";
            return placed.Any(p => p.Placement.Bounds.Size != p.Widget.LocalBounds.Size) ? "Resize" : "Move";
        }
    }

    /// A frame of `widgets`, the loaded theme's, with the dragged ones where the drag has them.
    public SKImage Render(ThemeRenderer renderer, IReadOnlyList<WidgetSpec> widgets, SensorSnapshot sensors, TimeSpan time)
    {
        _frame ??= Frame(widgets);
        return renderer.Render(sensors, time, _frame.Value.Widgets, _frame.Value.StandIns);
    }

    (IReadOnlyList<WidgetSpec>, IReadOnlyDictionary<WidgetSpec, WidgetSpec>) Frame(IReadOnlyList<WidgetSpec> widgets)
    {
        // Each dragged widget by the widget the theme draws for it, which is its WidgetRef's Spec.
        var placements = new Dictionary<WidgetSpec, WidgetPlacement>(ReferenceEqualityComparer.Instance);
        foreach (var (widget, placement) in _placed.Values) placements[widget.Spec] = placement;

        var standIns = new Dictionary<WidgetSpec, WidgetSpec>(ReferenceEqualityComparer.Instance);
        return (Placed(widgets, placements, standIns), standIns);
    }

    /// `widgets` with the placed ones, and the groups holding them, copied; the same list when none of them is.
    static IReadOnlyList<WidgetSpec> Placed(IReadOnlyList<WidgetSpec> widgets,
        Dictionary<WidgetSpec, WidgetPlacement> placements, Dictionary<WidgetSpec, WidgetSpec> standIns)
    {
        List<WidgetSpec>? copied = null;
        for (var i = 0; i < widgets.Count; i++)
        {
            var widget = widgets[i];
            var drawn = widget;
            if (widget is GroupWidget group && Placed(group.Children, placements, standIns) is var children
                && !ReferenceEquals(children, group.Children))
            {
                drawn = group with { Children = children };
            }

            if (placements.TryGetValue(widget, out var placement))
                drawn = drawn with { Bounds = placement.Bounds, Rotate = placement.Rotate };

            if (ReferenceEquals(drawn, widget)) continue;
            standIns[drawn] = widget;
            copied ??= [.. widgets];
            copied[i] = drawn;
        }

        return copied ?? widgets;
    }
}
