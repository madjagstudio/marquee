using System.Windows;

namespace Marquee.Editor.Canvas;

/// A selection handle in the view: Rotate is the round one above the box.
public readonly record struct HandleMark(Point At, bool IsRotate);

/// A guide line in the view.
public readonly record struct GuideLine(Point From, Point To);

/// What the canvas view draws over the frame, in its own coordinates (CanvasTransform): never part of the frame.
/// - Outlines: each selected widget's box as drawn, turned with it, as its four corners.
/// - Handles: the selection frame's resize and rotate handles, when the selection has a frame; RotateFrom is where the
///   rotate handle's stem starts (the top edge's middle).
/// - Guides: the magenta snap lines while a drag snaps.
/// - Marquee: the selection box being drawn.
/// - Group: the outline of the group being worked in.
public sealed record CanvasOverlay(
    IReadOnlyList<IReadOnlyList<Point>> Outlines,
    IReadOnlyList<HandleMark> Handles,
    Point? RotateFrom,
    IReadOnlyList<GuideLine> Guides,
    Rect? Marquee,
    IReadOnlyList<Point>? Group)
{
    public static CanvasOverlay Empty { get; } = new([], [], null, [], null, null);
}
