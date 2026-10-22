using System.Windows;
using System.Windows.Input;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;
using SkiaSharp;

namespace Marquee.Editor.Canvas;

/// The canvas's mouse and keys (spec Part B, Canvas), each through the model:
/// - click selects, Shift- or Ctrl-click adds or removes, and a drag on empty canvas draws a selection box;
/// - a drag on a widget moves the selection, a handle resizes or turns it (CanvasDrag); Alt places freely, and Shift
///   keeps a resize's proportions or snaps a turn to 15°; Esc cancels;
/// - double-click enters a group, and Esc, or a click outside it, leaves it;
/// - the arrow keys nudge (1 px, 10 with Shift); Delete, Ctrl+C, Ctrl+V, Ctrl+D, Ctrl+G and Ctrl+Shift+G work on the
///   selection, and Ctrl+Shift+C and Ctrl+Shift+V copy and paste style.
/// Positions come in the view's coordinates (CanvasTransform), from the canvas's top-left corner.
public sealed partial class CanvasViewModel
{
    /// How far, in the view's units, a click may miss a thin widget.
    const double HitSlop = 3;

    Gesture? _gesture;
    IReadOnlyList<SnapGuide> _guides = [];

    /// True while a move, resize or turn is under way.
    public bool IsDragging => _gesture is Dragging;

    /// The left button went down at `at`, with `modifiers` held; `clicks` is 2 for a double-click.
    public void PointerDown(Point at, ModifierKeys modifiers, int clicks)
    {
        if (Session is not { Theme: not null } session || Geometry() is not { } geometry) return;
        CancelGesture();
        if (session.SelectedLayer is not null) Select([]);
        var point = Transform.ToTheme(at);

        if (clicks >= 2)
        {
            if (geometry.GroupToEnter(point, _group) is { } entered)
            {
                Group = entered;
                Select([]);
            }

            return;
        }

        var additive = (modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
        if (!additive && CanEdit && SelectionFrame.Of(geometry, session.Selection) is { } frame
            && frame.HitHandle(new SKPoint((float)at.X, (float)at.Y), (float)Transform.Scale) is { } handle)
        {
            StartDrag(() => CanvasDrag.Grab(session, frame, handle, point), point, modifiers);
            return;
        }

        if (_group is not null) Group = geometry.GroupAfterClick(point, _group);
        var hit = geometry.HitTest(point, _group, (float)(HitSlop / Transform.Scale));
        if (hit is null)
        {
            _gesture = new Boxing(point, point, additive, additive ? session.Selection : []);
            if (!additive) Select([]);
            UpdateOverlay();
            return;
        }

        if (additive)
        {
            Select(session.Selection.Contains(hit.Path)
                ? session.Selection.Where(path => path != hit.Path).ToList()
                : [.. session.Selection, hit.Path]);
            return;
        }

        if (!session.Selection.Contains(hit.Path)) Select([hit.Path]);
        // A move waits until the mouse has gone as far as a handle must (CanvasDrag.StartDistance), so a click never
        // nudges the widget.
        if (CanEdit) _gesture = new Pressed(point, at);
    }

    /// The mouse moved to `at`, with `modifiers` held.
    public void PointerMove(Point at, ModifierKeys modifiers)
    {
        if (Session is not { } session) return;
        var point = Transform.ToTheme(at);
        switch (_gesture)
        {
            case Pressed pressed when (at - pressed.ViewStart).Length >= CanvasDrag.StartDistance:
                StartDrag(() => CanvasDrag.Move(session, session.Selection, pressed.Start), point, modifiers);
                break;
            case Dragging dragging:
                Update(dragging with { Last = point }, modifiers);
                break;
            case Boxing box:
                _gesture = box with { Current = point };
                UpdateOverlay();
                break;
        }
    }

    /// The left button came up at `at`.
    public void PointerUp(Point at, ModifierKeys modifiers)
    {
        var gesture = _gesture;
        _gesture = null;
        switch (gesture)
        {
            case Dragging dragging:
                _guides = [];
                Try(Strings.ActionDrag, () => dragging.Drag.Commit(), onFailure: dragging.Drag.Cancel);
                UpdateOverlay();
                break;
            case Boxing box when Geometry() is { } geometry:
                var end = Transform.ToTheme(at);
                var boxed = geometry.InBox(new SKRect(box.Start.X, box.Start.Y, end.X, end.Y), _group).Select(widget => widget.Path);
                Select(box.Additive ? [.. box.Before, .. boxed] : boxed.ToList());
                UpdateOverlay();
                break;
        }
    }

    /// The mouse was taken away mid-gesture (another window, a lost capture): a drag is cancelled.
    public void PointerLost() => CancelGesture();

    /// Shift or Alt went down or up during a drag: it follows at once, without waiting for the mouse to move.
    public void ModifiersChanged(ModifierKeys modifiers)
    {
        if (_gesture is Dragging dragging) Update(dragging, modifiers);
    }

    /// A key went down; true when the canvas used it.
    public bool KeyDown(Key key, ModifierKeys modifiers)
    {
        if (Session is not { } session) return false;
        if (_gesture is Dragging)
        {
            if (key == Key.Escape) CancelGesture();
            else ModifiersChanged(modifiers);
            return true;
        }

        var control = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        var selection = session.SelectedLayer is null ? session.Selection : [];
        switch (key)
        {
            case Key.Escape:
                Escape(session);
                return true;
            case Key.Left or Key.Right or Key.Up or Key.Down when !control:
                if (selection.Count > 0) _run(NudgeWidgets.Arrow(selection, Arrow(key), shift));
                return true;
            case Key.Delete when !control:
                DeleteSelection();
                return true;
            case Key.C when control && shift:
                CopyStyle();
                return true;
            case Key.V when control && shift:
                PasteStyle();
                return true;
            case Key.C when control:
                CopySelection();
                return true;
            case Key.V when control:
                Paste();
                return true;
            case Key.D when control:
                DuplicateSelection();
                return true;
            case Key.G when control && shift:
                UngroupSelection();
                return true;
            case Key.G when control:
                GroupSelection();
                return true;
            default:
                return false;
        }
    }

    /// Esc: out of the group being worked in, with that group selected, or, on the canvas, nothing selected.
    void Escape(EditorSession session)
    {
        if (_gesture is not null)
        {
            CancelGesture();
            return;
        }

        if (_group is { } group)
        {
            Group = CanvasGeometry.GroupOf(group);
            Select([group]);
            return;
        }

        if (session.Selection.Count > 0) Select([]);
    }

    void StartDrag(Func<CanvasDrag?> start, SKPoint point, ModifierKeys modifiers)
    {
        CanvasDrag? drag = null;
        Try(Strings.ActionDrag, () => drag = start());
        if (drag is null)
        {
            _gesture = null;
            return;
        }

        Update(new Dragging(drag, point), modifiers);
    }

    void Update(Dragging dragging, ModifierKeys modifiers)
    {
        _gesture = dragging;
        if (!Try(Strings.ActionDrag, () => _guides = dragging.Drag.Update(dragging.Last, Keys(modifiers), (float)Transform.Scale)))
        {
            CancelGesture();
            return;
        }

        UpdateOverlay();
    }

    void CancelGesture()
    {
        var gesture = _gesture;
        _gesture = null;
        _guides = [];
        if (gesture is Dragging dragging) dragging.Drag.Cancel();
        if (gesture is not null) UpdateOverlay();
    }

    void Select(IReadOnlyList<YamlPath> widgets)
    {
        if (Session is { } session) Try(Strings.ActionSelect, () => session.Select(widgets));
    }

    /// Runs `action`, reporting what the model refuses, as `what` failing. True when it went through.
    bool Try(string what, Action action, Action? onFailure = null)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ex is LayoutEditException or ThemeException or ArgumentException or InvalidOperationException
                                       or IOException or UnauthorizedAccessException)
        {
            onFailure?.Invoke();
            _report(Strings.ActionFailed(what, ex.Message));
            return false;
        }
    }

    static DragKeys Keys(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Shift) ? DragKeys.Shift : DragKeys.None)
        | (modifiers.HasFlag(ModifierKeys.Alt) ? DragKeys.Alt : DragKeys.None);

    static ArrowKey Arrow(Key key) => key switch
    {
        Key.Left => ArrowKey.Left,
        Key.Right => ArrowKey.Right,
        Key.Up => ArrowKey.Up,
        _ => ArrowKey.Down,
    };

    /// What the left button is doing.
    abstract record Gesture;

    /// Down on a widget, not yet moved far enough to move it.
    sealed record Pressed(SKPoint Start, Point ViewStart) : Gesture;

    /// A move, resize or turn, with the last mouse position, in theme pixels.
    sealed record Dragging(CanvasDrag Drag, SKPoint Last) : Gesture;

    /// A selection box from Start to Current, in theme pixels; Additive (Shift or Ctrl) adds what it takes to Before.
    sealed record Boxing(SKPoint Start, SKPoint Current, bool Additive, IReadOnlyList<YamlPath> Before) : Gesture;
}
