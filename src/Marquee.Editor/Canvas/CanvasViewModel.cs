using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Session;
using SkiaSharp;

namespace Marquee.Editor.Canvas;

/// The canvas: the open theme's frames in a bitmap, the zoom, the overlay drawn on top (selection, handles, guides,
/// the selection box, the group being worked in), and the mouse and keys turned into the model's commands and drags
/// (CanvasViewModel.Input.cs). CanvasView shows it and passes it its input; tests drive it directly.
///
/// Every position goes through one transform (CanvasTransform) from the view's coordinates to theme pixels, and the
/// model works in theme pixels from there.
public sealed partial class CanvasViewModel : ObservableObject, IDisposable
{
    readonly Func<EditorCommand, bool> _run;
    readonly IClipboardText _clipboard;
    readonly Action<string> _report;
    readonly TimeProvider _time;
    readonly RedrawScheduler _redraw;
    readonly StyleClipboard _styles = new();
    EditorWorkspace? _workspace;
    CanvasGeometry? _geometry;
    long _started;
    WriteableBitmap? _frame;
    ZoomMode _zoomMode = ZoomMode.Fit;
    Size _viewport;
    double _dpiScale = 1;
    CanvasTransform _transform = CanvasTransform.Identity;
    CanvasOverlay _overlay = CanvasOverlay.Empty;
    YamlPath? _group;

    /// `run` applies a command as the window does (reporting what goes wrong); `report` shows a message in the status
    /// bar. Made on the UI thread.
    public CanvasViewModel(Func<EditorCommand, bool> run, IClipboardText clipboard, Action<string> report, TimeProvider time)
    {
        _run = run;
        _clipboard = clipboard;
        _report = report;
        _time = time;
        _redraw = new RedrawScheduler(RenderNow, () => _workspace?.Values.Animates ?? false, time);
    }

    public EditorSession? Session => _workspace?.Session;

    /// Whether the mouse and keys may change the theme: not while it is read-only or editing is paused.
    public bool CanEdit => _workspace is { IsReadOnly: false, Session: { IsPaused: false, Theme: not null } };

    /// The last frame drawn; the same bitmap from frame to frame while the canvas keeps its size.
    public WriteableBitmap? Frame
    {
        get => _frame;
        private set => Set(ref _frame, value);
    }

    public ZoomMode ZoomMode
    {
        get => _zoomMode;
        set
        {
            if (Set(ref _zoomMode, value)) UpdateZoom();
        }
    }

    public CanvasTransform Transform
    {
        get => _transform;
        private set => Set(ref _transform, value);
    }

    /// The canvas's size in the view, at the zoom and DPI.
    public Size SurfaceSize => Transform.ViewSize(Session?.CanvasSize ?? SKSizeI.Empty);

    /// The zoom as the status bar shows it.
    public string ZoomText => string.Format(CultureInfo.CurrentCulture, "{0:0}%", Transform.Zoom * 100);

    public CanvasOverlay Overlay
    {
        get => _overlay;
        private set => Set(ref _overlay, value);
    }

    /// The group being worked in (double-click to enter, Esc to leave); null for the whole canvas. New and pasted widgets
    /// go into it.
    public YamlPath? Group
    {
        get => _group;
        private set
        {
            if (Set(ref _group, value)) UpdateOverlay();
        }
    }

    /// Draws the open theme from now on, from the first frame.
    public void Attach(EditorWorkspace workspace)
    {
        Detach();
        _workspace = workspace;
        _started = _time.GetTimestamp();
        _geometry = null;
        _group = null;
        workspace.Session.Changed += OnSessionChanged;
        workspace.Values.Updated += OnValuesUpdated;
        _redraw.Fps = workspace.Session.Theme?.Spec.Canvas.Fps ?? 30;
        workspace.Values.SetActive(_redraw.IsActive);
        UpdateZoom();
        Raise(nameof(Group));
        Raise(nameof(CanEdit));
        _redraw.Request();
    }

    /// Lets go of the theme, ending any drag.
    public void Detach()
    {
        if (_workspace is not { } workspace) return;
        CancelGesture();
        workspace.Session.Changed -= OnSessionChanged;
        workspace.Values.Updated -= OnValuesUpdated;
        _workspace = null;
        _geometry = null;
    }

    /// The space the canvas has in the window, and the screen's DPI scale (VisualTreeHelper.GetDpi), for Fit and for the
    /// transform.
    public void SetViewport(Size viewport, double dpiScale)
    {
        _viewport = viewport;
        _dpiScale = dpiScale > 0 ? dpiScale : 1;
        UpdateZoom();
    }

    /// Stops drawing and asking for values while the window is minimised; starts again when it is shown.
    public void SetVisible(bool visible)
    {
        _redraw.SetActive(visible);
        _workspace?.Values.SetActive(visible);
    }

    /// Draws a frame now, with the session's values, at the session's animation time.
    public void RenderNow()
    {
        if (Session is not { } session) return;
        using var image = session.RenderFrame(_time.GetElapsedTime(_started));
        if (image is not null) Frame = FrameBitmap.Copy(image, _frame);
    }

    public void Dispose()
    {
        Detach();
        _redraw.Dispose();
    }

    /// The widgets' places now, rebuilt after each change to the theme.
    CanvasGeometry? Geometry() =>
        _geometry ??= Session is { Theme: not null } session ? CanvasGeometry.Of(session) : null;

    void OnSessionChanged(object? sender, SessionChanges changes)
    {
        if (changes.HasFlag(SessionChanges.Theme))
        {
            _geometry = null;
            _redraw.Fps = Session?.Theme?.Spec.Canvas.Fps ?? 30;
            // An undo or ungroup can take away the group being worked in.
            if (_group is { } group && Geometry()?.Widget(group) is not { IsGroup: true }) Group = null;
            UpdateZoom();
            Raise(nameof(CanEdit));
        }

        UpdateOverlay();
        _redraw.Request();
    }

    void OnValuesUpdated(object? sender, EventArgs e) => _redraw.Request();

    void UpdateZoom()
    {
        var canvas = Session?.CanvasSize ?? SKSizeI.Empty;
        Transform = new CanvasTransform(CanvasTransform.ZoomFor(_zoomMode, _viewport, canvas, _dpiScale), _dpiScale);
        Raise(nameof(SurfaceSize));
        Raise(nameof(ZoomText));
        UpdateOverlay();
    }

    Point ToView(SKPoint theme) => Transform.ToView(theme);

    IReadOnlyList<Point> ToView(IEnumerable<SKPoint> theme) => theme.Select(ToView).ToList();

    /// Works out what to draw over the frame. During a drag the selection follows the preview: each dragged widget's
    /// outline is where the drag has it, and one widget keeps its handles.
    void UpdateOverlay()
    {
        if (Session is not { } session || Geometry() is not { } geometry)
        {
            Overlay = CanvasOverlay.Empty;
            return;
        }

        var placements = session.PreviewPlacements.ToDictionary(placement => placement.Widget);
        var outlines = session.Selection
            .Where(path => geometry.Widget(path) is not null)
            .Select(path => placements.TryGetValue(path, out var placed)
                ? ToView(Corners(geometry.ParentFrame(path), placed.Bounds, placed.Rotate))
                : ToView(geometry.CornersOf(path)))
            .ToList();

        var handles = new List<HandleMark>();
        Point? rotateFrom = null;
        if (_gesture is not Boxing && FrameOf(geometry, session, placements) is { } frame)
        {
            var scale = (float)Transform.Scale;
            handles.AddRange(frame.Handles(scale)
                .Select(handle => new HandleMark(new Point(handle.At.X, handle.At.Y), handle.Handle == SelectionHandle.Rotate)));
            if (frame.CanRotate) rotateFrom = ToView(frame.ToCanvas.MapPoint(frame.Box.MidX, frame.Box.Top));
        }

        var guides = _guides.Select(guide => new GuideLine(ToView(guide.From), ToView(guide.To))).ToList();
        Rect? marquee = _gesture is Boxing box ? new Rect(ToView(box.Start), ToView(box.Current)) : null;
        var groupOutline = _group is { } group && geometry.Widget(group) is not null ? ToView(geometry.CornersOf(group)) : null;
        Overlay = new CanvasOverlay(outlines, handles, rotateFrom, guides, marquee, groupOutline);
    }

    /// The selection's frame, following a drag of one widget; null while several are dragged (their outlines follow
    /// them, and the frame comes back when the drag ends), or when the selection has none.
    static SelectionFrame? FrameOf(CanvasGeometry geometry, EditorSession session, IReadOnlyDictionary<YamlPath, WidgetPlacement> placements)
    {
        var frame = SelectionFrame.Of(geometry, session.Selection);
        if (frame is null || placements.Count == 0) return frame;
        return frame.CanRotate && placements.TryGetValue(frame.Widgets[0].Path, out var placed)
            ? frame with { Box = placed.Bounds, Rotate = placed.Rotate }
            : null;
    }

    /// A box's corners on the canvas, as CanvasGeometry.CornersOf gives them, for a box the drag has moved: turned about
    /// its centre, in its group's coordinates.
    static SKPoint[] Corners(SKMatrix parent, SKRect box, float rotate) =>
        parent.PreConcat(SKMatrix.CreateRotationDegrees(rotate, box.MidX, box.MidY))
            .MapPoints([new(box.Left, box.Top), new(box.Right, box.Top), new(box.Right, box.Bottom), new(box.Left, box.Bottom)]);
}
