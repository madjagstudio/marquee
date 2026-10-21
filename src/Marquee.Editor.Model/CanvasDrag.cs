using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// The keys held during a drag: Shift keeps a resize's proportions or snaps a turn to 15°; Alt places freely, without
/// snapping.
[Flags]
public enum DragKeys
{
    None = 0,
    Shift = 1,
    Alt = 2,
}

/// One drag on the canvas, from the mouse going down to it coming up: a move of the selection, a resize by one of its
/// handles, or a turn by the rotate handle. The canvas view starts one (Move or Grab), passes each mouse position to
/// Update, in theme pixels, and ends it with Commit when the mouse comes up or Cancel on Esc.
///
/// It works through the session's preview (BeginPreview, PreviewMove, PreviewResize, PreviewRotate): frames show the
/// drag and the YAML is written once, as one undo step, on Commit. Positions are worked out on the canvas and written in
/// each widget's own coordinates, its group's, so a widget in a turned group goes where the mouse goes on screen.
/// Locked widgets, and those using an anchor or alias, aren't dragged. Whole pixels and degrees are written.
public sealed class CanvasDrag
{
    readonly EditorSession _session;
    readonly Func<SKPoint, DragKeys, float, IReadOnlyList<SnapGuide>> _update;

    CanvasDrag(EditorSession session, Func<SKPoint, DragKeys, float, IReadOnlyList<SnapGuide>> update)
    {
        _session = session;
        _update = update;
        session.BeginPreview();
    }

    /// Starts moving `widgets` (the selection) from `start`. A widget inside another being moved goes with it. Null when
    /// none of them can be moved. Throws InvalidOperationException as BeginPreview does.
    public static CanvasDrag? Move(EditorSession session, IReadOnlyList<YamlPath> widgets, SKPoint start)
    {
        var geometry = CanvasGeometry.Of(session);
        var moving = Arrange.Movable(geometry, widgets);
        if (moving.Count == 0) return null;

        var box = moving.Select(w => geometry.BoundsOf(w.Path)).Aggregate(SKRect.Union);
        var snapper = new Snapper(geometry, moving.Select(w => w.Path));
        var toParent = moving.Select(w => CanvasGeometry.Inverse(geometry.ParentFrame(w.Path))).ToList();
        return new CanvasDrag(session, (point, keys, zoom) =>
        {
            // Whole pixels are written, so the box moves by whole pixels and snaps (by whole pixels) as it will be written.
            var delta = new SKPoint(MathF.Round(point.X - start.X), MathF.Round(point.Y - start.Y));
            var snap = keys.HasFlag(DragKeys.Alt) ? SnapResult.None : snapper.Snap(Offset(box, delta), zoom);
            delta += snap.Offset;
            for (var i = 0; i < moving.Count; i++)
            {
                var local = toParent[i].MapVector(delta.X, delta.Y);
                session.PreviewMove(moving[i].Path, CanvasMath.Moved(moving[i].LocalBounds, local).Location);
            }

            return snap.Guides;
        });
    }

    /// Starts dragging `handle` of `frame` (SelectionFrame.HitHandle found it at `start`): Rotate turns the frame's one
    /// widget, any other handle resizes. Throws ArgumentException for Rotate on a frame of several widgets.
    public static CanvasDrag Grab(EditorSession session, SelectionFrame frame, SelectionHandle handle, SKPoint start)
    {
        if (handle == SelectionHandle.Rotate)
        {
            if (!frame.CanRotate) throw new ArgumentException("Only a single widget can be turned.", nameof(handle));
            return Rotate(session, frame, start);
        }

        return Resize(session, frame, handle, start);
    }

    /// Follows the mouse to `point`, in theme pixels, with `keys` held, at the canvas's `zoom` (for the snapping
    /// distance). Returns the snap guides to draw: none when nothing snapped.
    public IReadOnlyList<SnapGuide> Update(SKPoint point, DragKeys keys, float zoom) => _update(point, keys, zoom);

    /// Ends the drag, writing it as one undo step (EditorSession.CommitPreview); a drag that ends where it began writes
    /// nothing.
    public void Commit() => _session.CommitPreview();

    /// Ends the drag with everything as it was.
    public void Cancel() => _session.CancelPreview();

    /// How far, in screen pixels, a handle must be dragged before it resizes or turns anything: a handle pressed and let
    /// go, or a hand that shakes a pixel, leaves the widget as written, rather than rounding a fractional or turned box
    /// to whole pixels and degrees as an undo step.
    public const float StartDistance = 2;

    /// `update`, once the pointer has gone StartDistance from `start`; until then, nothing.
    static Func<SKPoint, DragKeys, float, IReadOnlyList<SnapGuide>> AfterStart(
        SKPoint start, Func<SKPoint, DragKeys, float, IReadOnlyList<SnapGuide>> update)
    {
        var started = false;
        return (point, keys, zoom) =>
        {
            started |= SKPoint.Distance(point, start) * zoom >= StartDistance;
            return started ? update(point, keys, zoom) : [];
        };
    }

    static CanvasDrag Rotate(EditorSession session, SelectionFrame frame, SKPoint start)
    {
        var widget = frame.Widgets[0];
        var centre = frame.ToCanvas.MapPoint(frame.Box.MidX, frame.Box.MidY);
        var from = CanvasMath.Angle(centre, start);
        return new CanvasDrag(session, AfterStart(start, (point, keys, _) =>
        {
            var turn = CanvasMath.Turn(widget.Rotate + CanvasMath.Angle(centre, point) - from, keys.HasFlag(DragKeys.Shift));
            session.PreviewRotate(widget.Path, turn);
            return [];
        }));
    }

    static CanvasDrag Resize(EditorSession session, SelectionFrame frame, SelectionHandle handle, SKPoint start)
    {
        var geometry = CanvasGeometry.Of(session);
        var box = frame.Box;
        var toFrame = CanvasGeometry.Inverse(frame.ToCanvas);
        // Where on the handle it was grabbed, so the edge doesn't jump to the mouse.
        var grab = SelectionFrame.PointOf(box, handle) - toFrame.MapPoint(start);
        var single = frame.Widgets.Count == 1;
        var min = single ? CanvasMath.MinSize(session.Document, frame.Widgets[0]) : 1;
        var mins = frame.Widgets.Select(w => CanvasMath.MinSize(session.Document, w)).ToList();
        // Snapping lines are upright, so only an upright box snaps.
        var upright = Math.Abs(frame.ToCanvas.SkewX) < 1e-6 && Math.Abs(frame.ToCanvas.SkewY) < 1e-6;
        var snapper = new Snapper(geometry, frame.Widgets.Select(w => w.Path));

        return new CanvasDrag(session, AfterStart(start, (point, keys, zoom) =>
        {
            var pointer = toFrame.MapPoint(point) + grab;
            var keep = keys.HasFlag(DragKeys.Shift);
            var resized = CanvasMath.Resize(box, handle, pointer, keep, min);
            var snap = SnapResult.None;
            // A snap would pull a proportional resize off its proportions, so Shift places freely too.
            if (upright && !keep && !keys.HasFlag(DragKeys.Alt))
            {
                snap = snapper.Snap(frame.ToCanvas.MapRect(resized), zoom, CanvasMath.EdgesOf(handle));
                if (snap.Offset != SKPoint.Empty) resized = CanvasMath.Resize(box, handle, pointer + snap.Offset, keep, min);
            }

            if (single)
            {
                session.PreviewResize(frame.Widgets[0].Path, CanvasMath.Placed(box, frame.Rotate, resized));
                return snap.Guides;
            }

            for (var i = 0; i < frame.Widgets.Count; i++)
                session.PreviewResize(frame.Widgets[i].Path, Scaled(frame.Widgets[i].LocalBounds, box, resized, mins[i]));
            return snap.Guides;
        }));
    }

    /// A widget's box within `from`, scaled with it to `to`, in whole pixels, at least `min` each way.
    static SKRect Scaled(SKRect widget, SKRect from, SKRect to, float min)
    {
        var sx = from.Width > 0 ? to.Width / from.Width : 1;
        var sy = from.Height > 0 ? to.Height / from.Height : 1;
        return SKRect.Create(
            MathF.Round(to.Left + (widget.Left - from.Left) * sx), MathF.Round(to.Top + (widget.Top - from.Top) * sy),
            Math.Max(min, MathF.Round(widget.Width * sx)), Math.Max(min, MathF.Round(widget.Height * sy)));
    }

    static SKRect Offset(SKRect box, SKPoint delta)
    {
        box.Offset(delta);
        return box;
    }
}
