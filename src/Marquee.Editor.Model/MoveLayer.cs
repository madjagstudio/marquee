using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Moves the widget at `Widget` in the layers: within its list (reordering, which changes what draws on top), or into
/// the `children` of the group at `Group`, or out to the top-level list when Group is null. `Index` is where it ends up
/// in that list, counted without it, in the file's order, back to front (LayoutDocument.MoveItem; LayersModel.Drop
/// works it out from the panel). It keeps its text and comments, and a widget changing groups has its `at` (and, for a
/// turned group, its `rotate`) rewritten so it doesn't move on screen. The moved widget becomes the selection. Throws
/// ArgumentException for a Group that isn't a group, or is the widget or inside it.
public sealed partial record MoveLayer(YamlPath Widget, YamlPath? Group, int Index) : EditorCommand
{
    public override string Label =>
        IsBackgroundMove ? "Move layer" :
        CanvasGeometry.GroupOf(Widget) == Group ? "Reorder" : Group is null ? "Move out of group" : "Move into group";

    public override CommandResult Run(CommandContext context)
    {
        if (IsBackgroundMove) return MoveBackground(context);
        var widget = context.RequireWidget(Widget);
        if (Group is not null)
        {
            if (!context.RequireWidget(Group).IsGroup) throw new ArgumentException($"{Group} isn't a group.", nameof(Group));
            if (Group == Widget || LayerEdits.Inside(Group, Widget))
                throw new ArgumentException("A group can't go inside itself.", nameof(Group));
        }

        var document = context.Document.MoveItem(Widget, LayerEdits.ListOf(Group), Index, out var moved);
        if (CanvasGeometry.GroupOf(Widget) != Group)
        {
            var (bounds, rotate) = LayerEdits.Rebased(CanvasGeometry.Of(context), widget, Group);
            document = PlaceWidgets.Place(document, moved, widget.LocalBounds, widget.Rotate, bounds, rotate);
        }

        return new CommandResult(document, [moved]);
    }
}
