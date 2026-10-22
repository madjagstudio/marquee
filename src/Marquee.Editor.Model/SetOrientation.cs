using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Changes the canvas orientation, optionally reflecting top-level widget centres across the diagonal. Groups move
/// as a whole; sizes, rotation and children's relative positions stay as written. The session makes this one undo.
public sealed record SetOrientation(string Orientation, bool Rearrange) : EditorCommand
{
    public override string Label => "Change orientation";

    public override CommandResult Run(CommandContext context)
    {
        var document = context.Document.SetScalar(YamlPath.Root.Child("canvas").Child("orientation"), Orientation);
        if (!Rearrange) return document;

        foreach (var widget in context.Widgets)
        {
            var box = widget.Bounds;
            var delta = new SKPoint(box.MidY - box.MidX, box.MidX - box.MidY);
            var moved = CanvasMath.Moved(widget.LocalBounds, delta);
            document = PlaceWidgets.Place(document, widget.Path, widget.LocalBounds, widget.Rotate, moved, widget.Rotate);
        }

        return document;
    }

    /// Counts top-level widget boxes that aren't wholly inside the current canvas, including groups only once.
    public static int PartlyOffCanvas(EditorSession session)
    {
        var size = session.CanvasSize;
        var canvas = new SKRect(0, 0, size.Width, size.Height);
        return session.Widgets.Count(widget => !canvas.Contains(widget.Bounds));
    }
}
