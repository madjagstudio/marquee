using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Where the open theme's widgets are on the canvas, for the mouse: what a click hits, what a selection box takes, and
/// how a group's coordinates (the ones its children's `at` is written in) map to the canvas's. Everything is in theme
/// pixels; the canvas view divides the mouse's position by its zoom first.
///
/// Each widget is placed as the renderer draws it (WidgetPainter): turned by its `rotate` about its own centre, and,
/// inside a group, moved to the group's corner, turned with the group and clipped to its box. A widget is hidden when
/// it or a group holding it is, and locked likewise: neither can be grabbed on the canvas.
///
/// A snapshot of one widget tree: the editor builds a new one after each change (Of).
public sealed partial class CanvasGeometry
{
    readonly IReadOnlyList<WidgetRef> _roots;
    readonly Dictionary<YamlPath, Placed> _placed = [];

    public CanvasGeometry(IReadOnlyList<WidgetRef> widgets, SKSizeI canvasSize)
    {
        _roots = widgets;
        CanvasSize = canvasSize;
        Add(widgets, null, SKMatrix.Identity, 0, false, false);
    }

    /// The session's widgets as they are now.
    public static CanvasGeometry Of(EditorSession session) => new(session.Widgets, session.CanvasSize);

    /// The geometry a command sees: its context's widgets.
    public static CanvasGeometry Of(CommandContext context) => new(context.Widgets, context.CanvasSize);

    /// The canvas in theme pixels.
    public SKSizeI CanvasSize { get; }

    /// The widget at `path`, or null when there is none.
    public WidgetRef? Widget(YamlPath path) => _placed.GetValueOrDefault(path)?.Widget;

    /// The widgets the canvas works with: the top-level ones, or those of the group at `group` while it is being worked
    /// in, back to front.
    public IReadOnlyList<WidgetRef> WidgetsIn(YamlPath? group) => group is null ? _roots : Require(group).Widget.Children;

    /// True when the widget at `path`, or a group holding it, is hidden: it isn't drawn.
    public bool IsHidden(YamlPath path) => Require(path).Hidden;

    /// True when the widget at `path`, or a group holding it, is locked: the mouse can't grab or move it.
    public bool IsLocked(YamlPath path) => Require(path).Locked;

    /// Maps the widget's box as `at` and `size` write it (LocalBounds) to the canvas: through its groups, and turned
    /// about its centre.
    public SKMatrix FrameOf(YamlPath widget) => Require(widget).Frame;

    /// Maps the coordinates `at` is written in inside the group at `group` (the canvas's own when null) to the canvas.
    public SKMatrix ChildFrame(YamlPath? group)
    {
        if (group is null) return SKMatrix.Identity;
        var placed = Require(group);
        return placed.Frame.PreConcat(SKMatrix.CreateTranslation(placed.Widget.LocalBounds.Left, placed.Widget.LocalBounds.Top));
    }

    /// Maps the coordinates `at` is written in for the widget at `widget` (its group's, or the canvas's) to the canvas.
    public SKMatrix ParentFrame(YamlPath widget) => ChildFrame(Require(widget).Group);

    /// A canvas point in the coordinates of the group at `group`, as its children's `at` is written.
    public SKPoint ToLocal(SKPoint canvasPoint, YamlPath? group) => Inverse(ChildFrame(group)).MapPoint(canvasPoint);

    /// A point in the group's coordinates on the canvas.
    public SKPoint ToCanvas(SKPoint local, YamlPath? group) => ChildFrame(group).MapPoint(local);

    /// How far the widget is turned on the canvas, in degrees clockwise: its own `rotate` and its groups'.
    public float TurnOf(YamlPath widget) => Require(widget).Turn;

    /// The corners of the widget's box on the canvas, as drawn: top-left, top-right, bottom-right and bottom-left, as
    /// they were before it turned. The selection's outline joins them.
    public SKPoint[] CornersOf(YamlPath widget)
    {
        var placed = Require(widget);
        var box = placed.Widget.LocalBounds;
        return placed.Frame.MapPoints([new(box.Left, box.Top), new(box.Right, box.Top), new(box.Right, box.Bottom),
            new(box.Left, box.Bottom)]);
    }

    /// The smallest upright rectangle around the widget's box on the canvas, as drawn: what snapping, aligning and a
    /// selection box use.
    public SKRect BoundsOf(YamlPath widget)
    {
        var placed = Require(widget);
        return placed.Frame.MapRect(placed.Widget.LocalBounds);
    }

    /// The front-most widget at `point` that the mouse can grab, among the top-level widgets or, with `group`, that
    /// group's children; null when there is none. Hidden and locked widgets are passed over, and a group's children are
    /// only there inside its box, which clips them. A group is hit anywhere in its box. `slop` widens each box by that
    /// many theme pixels, so a line 0 px thick can be clicked: the view passes a few screen pixels over its zoom.
    public WidgetRef? HitTest(SKPoint point, YamlPath? group = null, float slop = 0)
    {
        if (group is not null && !Contains(Require(group), point, 0)) return null;
        var widgets = WidgetsIn(group);
        for (var i = widgets.Count - 1; i >= 0; i--)
        {
            var placed = _placed[widgets[i].Path];
            if (Grabbable(placed) && Contains(placed, point, slop)) return placed.Widget;
        }

        return null;
    }

    /// The widgets the mouse can grab, among the top-level ones or `group`'s children, whose box on the canvas
    /// (BoundsOf) is wholly inside `box`, back to front. The box may be drawn in any direction.
    public IReadOnlyList<WidgetRef> InBox(SKRect box, YamlPath? group = null)
    {
        box = box.Standardized;
        return WidgetsIn(group).Where(w => Grabbable(_placed[w.Path]) && Inside(BoundsOf(w.Path), box)).ToList();
    }

    /// The group whose `children` hold the widget at `widget`, or null for a top-level widget: what Esc goes back to
    /// from inside a group.
    public static YamlPath? GroupOf(YamlPath widget) =>
        widget.Parent is { Last: { Key: "children" } } children ? children.Parent : null;

    static bool Grabbable(Placed placed) => !placed.Hidden && !placed.Locked;

    static bool Contains(Placed placed, SKPoint point, float slop)
    {
        var local = Inverse(placed.Frame).MapPoint(point);
        var box = placed.Widget.LocalBounds;
        box.Inflate(slop, slop);
        return local.X >= box.Left && local.X <= box.Right && local.Y >= box.Top && local.Y <= box.Bottom;
    }

    /// True when `inner` is inside `outer`, give or take the float error a turn leaves.
    static bool Inside(SKRect inner, SKRect outer)
    {
        const float Error = 0.001f;
        return inner.Left >= outer.Left - Error && inner.Top >= outer.Top - Error && inner.Right <= outer.Right + Error
            && inner.Bottom <= outer.Bottom + Error;
    }

    /// The matrix's inverse: a frame only moves and turns, so it always has one.
    internal static SKMatrix Inverse(SKMatrix matrix) =>
        matrix.TryInvert(out var inverse) ? inverse : throw new InvalidOperationException("A widget's frame can't be inverted.");

    Placed Require(YamlPath path) =>
        _placed.GetValueOrDefault(path) ?? throw new ArgumentException($"There is no widget at {path}.", nameof(path));

    void Add(IReadOnlyList<WidgetRef> widgets, YamlPath? group, SKMatrix parent, float turn, bool hidden, bool locked)
    {
        foreach (var widget in widgets)
        {
            var box = widget.LocalBounds;
            var frame = parent.PreConcat(SKMatrix.CreateRotationDegrees(widget.Rotate, box.MidX, box.MidY));
            var placed = new Placed(widget, group, frame, turn + widget.Rotate, hidden || widget.Hidden, locked || widget.Locked);
            _placed[widget.Path] = placed;
            Add(widget.Children, widget.Path, frame.PreConcat(SKMatrix.CreateTranslation(box.Left, box.Top)), placed.Turn,
                placed.Hidden, placed.Locked);
        }
    }

    /// A widget with its group, its frame (FrameOf), its turn on the canvas, and whether it is hidden or locked there.
    sealed record Placed(WidgetRef Widget, YamlPath? Group, SKMatrix Frame, float Turn, bool Hidden, bool Locked);
}
