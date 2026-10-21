using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// The sums behind dragging: a box resized by a handle, a turn snapped, a box placed where a turned one was. All in
/// theme pixels and degrees. Themes are written in whole pixels and degrees, so what a drag writes is rounded to them.
static class CanvasMath
{
    /// The step Shift snaps a turn to.
    public const float RotateStep = 15;

    /// The smallest box a widget of its type can have, each way: 1 px, or the catalog's lower MinFor where the widget is
    /// in that form (a line shape's may be 0).
    public static float MinSize(LayoutDocument document, WidgetRef widget)
    {
        var size = ThemeSchema.WidgetKeys(widget.Type).FirstOrDefault(key => key.Name == "size");
        if (size?.MinFor is { } minFor && document.TryGetScalar(widget.Path.Child(minFor.Key), out var written)
            && written == minFor.Value)
        {
            return (float)minFor.Min;
        }

        return (float)Math.Max(1, size?.Min ?? 1);
    }

    /// `box` (upright, in its own unturned frame) resized by dragging `handle` to `pointer` in that frame. The opposite
    /// edge or corner stays where it is, and the box doesn't turn inside out: each side stops `min` from the one that
    /// stays. With `keepProportions` the box keeps its width to height: a corner takes whichever side grew more, and an
    /// edge's handle grows the other side about the box's middle line. Sizes are whole pixels.
    public static SKRect Resize(SKRect box, SelectionHandle handle, SKPoint pointer, bool keepProportions, float min)
    {
        var (moveLeft, moveRight) = (IsLeft(handle), IsRight(handle));
        var (moveTop, moveBottom) = (IsTop(handle), IsBottom(handle));
        var width = moveRight ? pointer.X - box.Left : moveLeft ? box.Right - pointer.X : box.Width;
        var height = moveBottom ? pointer.Y - box.Top : moveTop ? box.Bottom - pointer.Y : box.Height;
        (width, height) = (Math.Max(min, MathF.Round(width)), Math.Max(min, MathF.Round(height)));

        if (keepProportions && box.Width > 0 && box.Height > 0)
        {
            var scale = (moveLeft || moveRight, moveTop || moveBottom) switch
            {
                (true, true) => Math.Max(width / box.Width, height / box.Height),
                (true, false) => width / box.Width,
                _ => height / box.Height,
            };
            scale = Math.Max(scale, min / Math.Min(box.Width, box.Height));
            (width, height) = (MathF.Round(box.Width * scale), MathF.Round(box.Height * scale));
        }

        // A line can be flat one way, never both.
        if (width <= 0 && height <= 0) width = 1;

        var left = moveLeft ? box.Right - width : moveRight ? box.Left : box.MidX - width / 2;
        var top = moveTop ? box.Bottom - height : moveBottom ? box.Top : box.MidY - height / 2;
        return SKRect.Create(left, top, width, height);
    }

    /// Where `resized` goes, in the parent's coordinates, for a box `box` turned `rotate` degrees about its centre that
    /// was resized, unturned, to `resized` (Resize): the edge or corner that stayed put stays put on screen too, though
    /// the turn's centre has moved. The corner is rounded to whole pixels.
    public static SKRect Placed(SKRect box, float rotate, SKRect resized)
    {
        var centre = SKMatrix.CreateRotationDegrees(rotate, box.MidX, box.MidY).MapPoint(resized.MidX, resized.MidY);
        return SKRect.Create(MathF.Round(centre.X - resized.Width / 2), MathF.Round(centre.Y - resized.Height / 2),
            resized.Width, resized.Height);
    }

    /// A turn as written: Shift snaps it to RotateStep, and otherwise it is whole degrees; between -180 and 180.
    public static float Turn(float degrees, bool snap)
    {
        var step = snap ? RotateStep : 1;
        return Normalised(MathF.Round(degrees / step) * step);
    }

    /// The same turn between -180 (not included) and 180 degrees.
    public static float Normalised(float degrees)
    {
        var turn = degrees % 360;
        return turn > 180 ? turn - 360 : turn <= -180 ? turn + 360 : turn;
    }

    /// `box` moved by `delta`, its corner rounded to whole pixels on each axis that moves, so a number the move doesn't
    /// change stays as written.
    public static SKRect Moved(SKRect box, SKPoint delta)
    {
        const float Still = 0.0001f;
        var x = Math.Abs(delta.X) < Still ? box.Left : MathF.Round(box.Left + delta.X);
        var y = Math.Abs(delta.Y) < Still ? box.Top : MathF.Round(box.Top + delta.Y);
        return SKRect.Create(x, y, box.Width, box.Height);
    }

    /// The angle of `point` about `centre`, in degrees clockwise from 3 o'clock.
    public static float Angle(SKPoint centre, SKPoint point) =>
        MathF.Atan2(point.Y - centre.Y, point.X - centre.X) * 180 / MathF.PI;

    static bool IsLeft(SelectionHandle handle) =>
        handle is SelectionHandle.TopLeft or SelectionHandle.Left or SelectionHandle.BottomLeft;

    static bool IsRight(SelectionHandle handle) =>
        handle is SelectionHandle.TopRight or SelectionHandle.Right or SelectionHandle.BottomRight;

    static bool IsTop(SelectionHandle handle) =>
        handle is SelectionHandle.TopLeft or SelectionHandle.Top or SelectionHandle.TopRight;

    static bool IsBottom(SelectionHandle handle) =>
        handle is SelectionHandle.BottomLeft or SelectionHandle.Bottom or SelectionHandle.BottomRight;

    /// The edges a handle moves, for snapping a resize.
    public static SnapEdges EdgesOf(SelectionHandle handle) =>
        (IsLeft(handle) ? SnapEdges.Left : 0) | (IsRight(handle) ? SnapEdges.Right : 0)
        | (IsTop(handle) ? SnapEdges.Top : 0) | (IsBottom(handle) ? SnapEdges.Bottom : 0);
}
