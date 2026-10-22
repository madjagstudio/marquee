using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfCanvas = System.Windows.Controls.Canvas;

namespace Marquee.Editor.Canvas;

/// Shows a CanvasViewModel: its frame, zoomed, with the overlay drawn as WPF shapes on top, and passes it the mouse
/// in its own coordinates (from the canvas's top-left corner) and its viewport and DPI. Keys come from the window.
public partial class CanvasView : UserControl
{
    /// The space around the canvas, in WPF units, on each side.
    public const double Gutter = 16;

    const double HandleSize = 8;
    const double RotateHandleSize = 10;

    Brush SelectionBrush => (Brush)FindResource("Red");
    Brush GuideBrush => (Brush)FindResource("RedGlow");
    Brush GroupBrush => (Brush)FindResource("TextMuted");
    Brush HandleFill => (Brush)FindResource("Red");
    Brush MarqueeFill => (Brush)FindResource("SelectionFill");
    static readonly DoubleCollection Dashes = FrozenDashes(4, 3);

    CanvasViewModel? _model;

    public CanvasView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Bind(e.NewValue as CanvasViewModel);
        Loaded += (_, _) => UpdateViewport();
        Scroller.SizeChanged += (_, _) => UpdateViewport();
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        LostMouseCapture += (_, _) => _model?.PointerLost();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateViewport();
    }

    void Bind(CanvasViewModel? model)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (model is null) return;
        model.PropertyChanged += OnModelChanged;
        UpdateViewport();
        UpdateScaling();
        DrawOverlay();
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CanvasViewModel.Overlay):
                DrawOverlay();
                break;
            case nameof(CanvasViewModel.Transform):
                UpdateScaling();
                break;
        }
    }

    /// The room the scroller gives the canvas, less the gutter, and the screen's DPI. A unit less again, so layout
    /// rounding a fitted canvas up by a pixel never brings the scroll bars out.
    void UpdateViewport()
    {
        if (_model is null) return;
        var spare = 2 * Gutter + 1;
        var room = new Size(Math.Max(0, Scroller.ActualWidth - spare), Math.Max(0, Scroller.ActualHeight - spare));
        _model.SetViewport(room, VisualTreeHelper.GetDpi(this).DpiScaleX);
    }

    /// Whole screen pixels per theme pixel stay sharp, for placing by eye at 100% and 200%; below that, smooth.
    void UpdateScaling() =>
        RenderOptions.SetBitmapScalingMode(FrameImage,
            _model?.Transform.Zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_model is null) return;
        Focus();
        CaptureMouse();
        _model.PointerDown(e.GetPosition(Surface), Keyboard.Modifiers, e.ClickCount);
        e.Handled = true;
    }

    void OnMouseMove(object sender, MouseEventArgs e) => _model?.PointerMove(e.GetPosition(Surface), Keyboard.Modifiers);

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_model is null) return;
        // Before letting the mouse go, which would cancel the drag (LostMouseCapture).
        _model.PointerUp(e.GetPosition(Surface), Keyboard.Modifiers);
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    void DrawOverlay()
    {
        Overlay.Children.Clear();
        if (_model is null) return;
        var overlay = _model.Overlay;

        if (overlay.Group is { } group)
            Overlay.Children.Add(new Polygon { Points = new PointCollection(group), Stroke = GroupBrush, StrokeThickness = 1, StrokeDashArray = Dashes });
        foreach (var outline in overlay.Outlines)
            Overlay.Children.Add(new Polygon { Points = new PointCollection(outline), Stroke = SelectionBrush, StrokeThickness = 1 });
        foreach (var guide in overlay.Guides)
            Overlay.Children.Add(LineBetween(guide.From, guide.To, GuideBrush));

        if (overlay.RotateFrom is { } stem && overlay.Handles.FirstOrDefault(handle => handle.IsRotate) is { IsRotate: true } rotate)
            Overlay.Children.Add(LineBetween(stem, rotate.At, SelectionBrush));
        foreach (var handle in overlay.Handles)
        {
            var size = handle.IsRotate ? RotateHandleSize : HandleSize;
            Shape shape = handle.IsRotate ? new Ellipse() : new Rectangle();
            shape.Width = size;
            shape.Height = size;
            shape.Fill = HandleFill;
            shape.Stroke = SelectionBrush;
            shape.StrokeThickness = 1;
            Place(shape, handle.At.X - size / 2, handle.At.Y - size / 2);
        }

        if (overlay.Marquee is { } box)
        {
            var marquee = new Rectangle
            {
                Width = box.Width,
                Height = box.Height,
                Fill = MarqueeFill,
                Stroke = SelectionBrush,
                StrokeThickness = 1,
                StrokeDashArray = Dashes,
            };
            Place(marquee, box.X, box.Y);
        }
    }

    void Place(UIElement element, double left, double top)
    {
        WpfCanvas.SetLeft(element, left);
        WpfCanvas.SetTop(element, top);
        Overlay.Children.Add(element);
    }

    static Line LineBetween(Point from, Point to, Brush brush) =>
        new() { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = brush, StrokeThickness = 1 };

    static DoubleCollection FrozenDashes(params double[] dashes)
    {
        var collection = new DoubleCollection(dashes);
        collection.Freeze();
        return collection;
    }
}
