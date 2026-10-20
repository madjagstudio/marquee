using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// The handles drawn around a selection, which drag to resize it or turn it.
public enum SelectionHandle { TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Rotate }

/// A handle and where it is on screen.
public readonly record struct HandlePoint(SelectionHandle Handle, SKPoint At);

/// The box the selection's handles go round, and the widgets they resize:
/// - one widget: its own box, turned with it (and its groups), with the rotate handle above it;
/// - several widgets in one list (siblings): the upright box round them all in their group's coordinates, with no
///   rotate handle, and resizing scales each widget's box within it.
///
/// Widgets the mouse can't move (locked, or using an anchor or alias) are left out; a selection that leaves none, or
/// whose widgets are in different lists, has no frame (Of is null) and can only be moved.
///
/// Screen pixels are theme pixels times the zoom, from the canvas's top-left corner: handles stay the same size on
/// screen at any zoom.
public sealed record SelectionFrame(IReadOnlyList<WidgetRef> Widgets, SKRect Box, float Rotate, SKMatrix ParentToCanvas)
{
    /// How far a handle can be missed by, in screen pixels, each way: handles are drawn as squares about this wide.
    public const float HandleRadius = 6;

    /// How far above the box's top edge the rotate handle is, in screen pixels.
    public const float RotateHandleGap = 24;

    /// The frame round `selection`'s movable widgets, or null (the class comment says when).
    public static SelectionFrame? Of(CanvasGeometry geometry, IReadOnlyList<YamlPath> selection)
    {
        var widgets = selection.Distinct().Select(path => geometry.Widget(path))
            .OfType<WidgetRef>()
            .Where(widget => widget.IsEditable && !geometry.IsLocked(widget.Path))
            .ToList();
        if (widgets.Count == 0) return null;
        var group = CanvasGeometry.GroupOf(widgets[0].Path);
        if (widgets.Any(widget => CanvasGeometry.GroupOf(widget.Path) != group)) return null;

        var parent = geometry.ChildFrame(group);
        if (widgets.Count == 1) return new SelectionFrame(widgets, widgets[0].LocalBounds, widgets[0].Rotate, parent);

        // Each box as drawn in the group's coordinates, before the group turns it.
        var box = widgets.Select(widget => Turned(widget).MapRect(widget.LocalBounds)).Aggregate(SKRect.Union);
        return new SelectionFrame(widgets, box, 0, parent);
    }

    /// One widget can be turned; several can't.
    public bool CanRotate => Widgets.Count == 1;

    /// Maps the box, upright as Box gives it, to the canvas.
    public SKMatrix ToCanvas => ParentToCanvas.PreConcat(SKMatrix.CreateRotationDegrees(Rotate, Box.MidX, Box.MidY));

    /// The handles in screen pixels: the corners and edge midpoints, then the rotate handle when there is one.
    public IReadOnlyList<HandlePoint> Handles(float zoom)
    {
        var screen = Screen(zoom);
        var handles = Enum.GetValues<SelectionHandle>()
            .Where(handle => handle != SelectionHandle.Rotate)
            .Select(handle => new HandlePoint(handle, screen.MapPoint(PointOf(Box, handle))))
            .ToList();
        if (!CanRotate) return handles;

        // Straight up from the top edge's middle, as the box is turned.
        var up = screen.MapVector(0, -1);
        var length = up.Length;
        var top = screen.MapPoint(PointOf(Box, SelectionHandle.Top));
        handles.Add(new HandlePoint(SelectionHandle.Rotate,
            new SKPoint(top.X + up.X / length * RotateHandleGap, top.Y + up.Y / length * RotateHandleGap)));
        return handles;
    }

    /// The handle at `screenPoint`, or null for none, where a press inside the box moves it:
    /// - a handle reaches HandleRadius each way, and where several reach the press, the nearest is hit (the rotate
    ///   handle, then the corners, then the edges, where they are as near);
    /// - inside the box, a handle reaches no further into it than a quarter of the box's width or height on screen, so
    ///   the middle half of a box smaller than its handles (an icon at "fit" zoom) still moves it, and a press right by
    ///   a corner still grabs that corner. A box 4 × HandleRadius across or more is reached the full HandleRadius.
    public SelectionHandle? HitHandle(SKPoint screenPoint, float zoom)
    {
        var screen = Screen(zoom);
        var inverts = screen.TryInvert(out var toBox);
        var local = toBox.MapPoint(screenPoint);
        var inside = inverts && local.X > Box.Left && local.X < Box.Right && local.Y > Box.Top && local.Y < Box.Bottom;
        // Screen pixels per theme pixel along the box's own sides, which turn with it.
        var (scaleX, scaleY) = (screen.MapVector(1, 0).Length, screen.MapVector(0, 1).Length);
        var reachX = Math.Min(HandleRadius, Box.Width * scaleX / 4);
        var reachY = Math.Min(HandleRadius, Box.Height * scaleY / 4);

        HandlePoint? nearest = null;
        var distance = float.MaxValue;
        foreach (var handle in Handles(zoom).OrderBy(h => Priority(h.Handle)))
        {
            var reaches = inside
                ? handle.Handle != SelectionHandle.Rotate
                    && Math.Abs(local.X - PointOf(Box, handle.Handle).X) * scaleX <= reachX
                    && Math.Abs(local.Y - PointOf(Box, handle.Handle).Y) * scaleY <= reachY
                : Math.Abs(handle.At.X - screenPoint.X) <= HandleRadius && Math.Abs(handle.At.Y - screenPoint.Y) <= HandleRadius;
            var away = SKPoint.Distance(handle.At, screenPoint);
            if (!reaches || away >= distance) continue;
            (nearest, distance) = (handle, away);
        }

        return nearest?.Handle;
    }

    /// Maps the box, upright as Box gives it, to screen pixels at `zoom`.
    SKMatrix Screen(float zoom) => SKMatrix.CreateScale(zoom, zoom).PreConcat(ToCanvas);

    /// Where `handle` is on `box`, upright: a corner or an edge's middle (the rotate handle's edge is the top).
    internal static SKPoint PointOf(SKRect box, SelectionHandle handle) => handle switch
    {
        SelectionHandle.TopLeft => new(box.Left, box.Top),
        SelectionHandle.Top or SelectionHandle.Rotate => new(box.MidX, box.Top),
        SelectionHandle.TopRight => new(box.Right, box.Top),
        SelectionHandle.Right => new(box.Right, box.MidY),
        SelectionHandle.BottomRight => new(box.Right, box.Bottom),
        SelectionHandle.Bottom => new(box.MidX, box.Bottom),
        SelectionHandle.BottomLeft => new(box.Left, box.Bottom),
        SelectionHandle.Left => new(box.Left, box.MidY),
        _ => throw new ArgumentOutOfRangeException(nameof(handle)),
    };

    /// A widget's own turn about its centre, in its group's coordinates.
    internal static SKMatrix Turned(WidgetRef widget) =>
        SKMatrix.CreateRotationDegrees(widget.Rotate, widget.LocalBounds.MidX, widget.LocalBounds.MidY);

    static int Priority(SelectionHandle handle) => handle switch
    {
        SelectionHandle.Rotate => 0,
        SelectionHandle.TopLeft or SelectionHandle.TopRight or SelectionHandle.BottomRight or SelectionHandle.BottomLeft => 1,
        _ => 2,
    };
}
