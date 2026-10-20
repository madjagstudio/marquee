using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// A drag on the canvas (move, resize, rotate). Frames draw the dragged widgets where the drag has them, from the
/// loaded theme changed in memory, and the YAML is written once, when the drag ends:
/// 1. BeginPreview when the mouse goes down on a handle or a widget;
/// 2. PreviewMove, PreviewResize or PreviewRotate as it moves, for each widget dragged, each call giving the widget's
///    whole new `at`, box or turn (not a change from the last), in its group's coordinates, as layout.yaml writes them:
///    the canvas geometry works them out from the mouse;
/// 3. CommitPreview when it is let go, which writes the changed numbers as one undo step (PlaceWidgets), or
///    CancelPreview on Esc, which puts everything back.
///
/// Commands, undo and redo wait until it ends. A hidden widget can be dragged (from the layers panel's selection) but
/// isn't drawn; one that uses an anchor or alias can't be dragged at all. Locked widgets aren't refused here: the
/// canvas doesn't offer them to the mouse.
public sealed partial class EditorSession
{
    DragPreview? _preview;

    public bool IsPreviewing => _preview is not null;

    /// Starts a drag. Throws InvalidOperationException during another one, or while editing is paused.
    public void BeginPreview()
    {
        ThrowIfDisposed();
        if (IsPreviewing) throw new InvalidOperationException("A drag is already being previewed.");
        ThrowIfInteracting();
        if (IsPaused || Theme is null)
            throw new InvalidOperationException($"Editing is paused until layout.yaml loads as a theme again: {LoadError}");
        _preview = new DragPreview();
        Raise(SessionChanges.Preview);
    }

    /// Moves the widget at `widget` so its `at` is `at`, keeping its size.
    public void PreviewMove(YamlPath widget, SKPoint at) =>
        Preview(widget, placement => placement with { Bounds = SKRect.Create(at, placement.Bounds.Size) });

    /// Gives the widget at `widget` the box `bounds`: its `at` and `size`.
    public void PreviewResize(YamlPath widget, SKRect bounds) => Preview(widget, placement => placement with { Bounds = bounds });

    /// Turns the widget at `widget` to `degrees` clockwise about its centre: its `rotate`.
    public void PreviewRotate(YamlPath widget, float degrees) => Preview(widget, placement => placement with { Rotate = degrees });

    /// Ends the drag, writing what it changed as one undo step, labelled Move, Resize or Rotate. A drag that ends where
    /// it began is no step. If the file refuses the edit, the drag is cancelled and the exception thrown.
    public void CommitPreview()
    {
        ThrowIfDisposed();
        var preview = _preview ?? throw new InvalidOperationException("No drag is being previewed.");
        _preview = null;
        if (preview.Placements is not { Count: > 0 } placements)
        {
            Raise(SessionChanges.Preview);
            return;
        }

        try
        {
            Apply(new PlaceWidgets(placements) { Description = preview.Description });
        }
        catch
        {
            Raise(SessionChanges.Preview);
            throw;
        }
    }

    /// Ends the drag with everything as it was. Does nothing when there is no drag, so Esc can always call it.
    public void CancelPreview()
    {
        ThrowIfDisposed();
        if (_preview is null) return;
        _preview = null;
        Raise(SessionChanges.Preview);
    }

    void Preview(YamlPath path, Func<WidgetPlacement, WidgetPlacement> change)
    {
        ThrowIfDisposed();
        var preview = _preview ?? throw new InvalidOperationException("No drag is being previewed: call BeginPreview first.");
        var widget = _tree.Find(path) ?? throw new ArgumentException($"There is no widget at {path}.", nameof(path));
        if (!widget.IsEditable)
        {
            throw new InvalidOperationException(
                $"{path} uses an anchor (&) or alias (*), so moving it would move the other places that use it too. " +
                "Edit it in the file.");
        }

        preview.Place(widget, change);
        Raise(SessionChanges.Preview);
    }
}
