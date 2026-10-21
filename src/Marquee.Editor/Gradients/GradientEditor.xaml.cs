using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Marquee.Editor.Controls;
using Marquee.Editor.Model;
using WpfCanvas = System.Windows.Controls.Canvas;

namespace Marquee.Editor.Gradients;

/// Shows a GradientEditorViewModel and turns the mouse into its calls: the stop bar (click to add, drag to move, drag
/// off or Del to remove), the chosen stop's colour, the angle dial (Shift snaps to 15°) and the radial preview's
/// centre and radius. The maths are the view-model's.
public partial class GradientEditor : UserControl
{
    /// How near a stop, in WPF units, a press on the bar must be to grab it rather than add one.
    const double GrabReach = 7;

    /// The radial preview's longer side, in WPF units.
    const double PreviewSize = 132;

    GradientEditorViewModel? _model;
    Grabbed _grabbed;
    bool _showing;

    public GradientEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Bind(e.NewValue as GradientEditorViewModel);
        Bar.SizeChanged += (_, _) => Redraw();
    }

    enum Grabbed { None, Stop, Dial, Center, Radius }

    void Bind(GradientEditorViewModel? model)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (model is not null) model.PropertyChanged += OnModelChanged;
        Redraw();
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Redraw();

    void Redraw()
    {
        if (_model is not { } model) return;
        _showing = true;
        try
        {
            LinearChoice.IsChecked = model.IsLinear;
            RadialChoice.IsChecked = model.IsRadial;
            StopColor.Color = model.SelectedColor;
        }
        finally
        {
            _showing = false;
        }

        DrawBar(model);
        DrawDial(model);
        DrawPreview(model);
    }

    // --- The stop bar ---

    void DrawBar(GradientEditorViewModel model)
    {
        Ramp.Background = GradientEditorViewModel.BrushOf(model.Gradient.WithKind(GradientKind.Linear).WithAngle(0));
        StopMarks.Children.Clear();
        var width = StopMarks.ActualWidth;
        foreach (var stop in model.Stops)
        {
            ColorText.TryParse(stop.Color, out var color);
            var mark = new Polygon
            {
                Points = [new Point(0, 0), new Point(6, 7), new Point(6, 15), new Point(-6, 15), new Point(-6, 7)],
                Fill = new SolidColorBrush(color),
                Stroke = stop.IsSelected ? Brushes.DodgerBlue : Brushes.DimGray,
                StrokeThickness = stop.IsSelected ? 2 : 1,
                Opacity = stop.IsLeaving ? 0.35 : 1,
            };
            WpfCanvas.SetLeft(mark, stop.Position * width);
            WpfCanvas.SetTop(mark, 18);
            StopMarks.Children.Add(mark);
        }
    }

    void OnBarDown(object sender, MouseButtonEventArgs e)
    {
        if (_model is not { } model) return;
        Bar.Focus();
        var at = e.GetPosition(StopMarks);
        var width = StopMarks.ActualWidth;
        var near = model.Stops.Select(stop => (stop.Index, Distance: Math.Abs(stop.Position * width - at.X)))
            .Where(stop => stop.Distance <= GrabReach)
            .OrderBy(stop => stop.Distance)
            .Select(stop => (int?)stop.Index)
            .FirstOrDefault();
        if (near is { } index)
        {
            model.BeginStopDrag(index);
            _grabbed = Grabbed.Stop;
            Bar.CaptureMouse();
        }
        else
        {
            model.AddStopAt(GradientEditorViewModel.PositionAt(at.X, width));
        }

        e.Handled = true;
    }

    void OnBarMove(object sender, MouseEventArgs e)
    {
        if (_grabbed != Grabbed.Stop || _model is not { } model) return;
        var at = e.GetPosition(StopMarks);
        // Away from the bar's middle, where the stops sit.
        model.DragStop(GradientEditorViewModel.PositionAt(at.X, StopMarks.ActualWidth), at.Y - Bar.ActualHeight / 2);
    }

    void OnBarUp(object sender, MouseButtonEventArgs e)
    {
        if (_grabbed != Grabbed.Stop) return;
        _grabbed = Grabbed.None;
        Bar.ReleaseMouseCapture();
        _model?.EndStopDrag();
        e.Handled = true;
    }

    void OnBarLost(object sender, MouseEventArgs e)
    {
        if (_grabbed != Grabbed.Stop) return;
        _grabbed = Grabbed.None;
        _model?.EndStopDrag();
    }

    void OnBarKeyDown(object sender, KeyEventArgs e)
    {
        if (_model is not { } model) return;
        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                model.RemoveSelected();
                e.Handled = true;
                break;
            case Key.Left:
                model.Selected--;
                e.Handled = true;
                break;
            case Key.Right:
                model.Selected++;
                e.Handled = true;
                break;
        }
    }

    void OnStopColorChanging(object? sender, ColorPickedEventArgs e)
    {
        if (!_showing) _model?.SetColor(_model.Selected, e.Hex, final: false);
    }

    void OnStopColorCommitted(object? sender, ColorPickedEventArgs e)
    {
        if (!_showing) _model?.SetColor(_model.Selected, e.Hex, final: true);
    }

    void OnSpreadEvenly(object sender, RoutedEventArgs e) => _model?.SpreadEvenly();

    void OnKindChecked(object sender, RoutedEventArgs e)
    {
        if (_showing || _model is not { } model) return;
        model.Kind = ReferenceEquals(sender, RadialChoice) ? GradientKind.Radial : GradientKind.Linear;
    }

    // --- The angle dial ---

    void DrawDial(GradientEditorViewModel model)
    {
        var radians = model.Angle * Math.PI / 180;
        var centre = Dial.Width / 2;
        DialHand.X2 = centre + Math.Cos(radians) * (centre - 4);
        DialHand.Y2 = centre + Math.Sin(radians) * (centre - 4);
    }

    void OnDialDown(object sender, MouseButtonEventArgs e)
    {
        _grabbed = Grabbed.Dial;
        Dial.CaptureMouse();
        TurnDial(e, final: false);
        e.Handled = true;
    }

    void OnDialMove(object sender, MouseEventArgs e)
    {
        if (_grabbed == Grabbed.Dial) TurnDial(e, final: false);
    }

    void OnDialUp(object sender, MouseButtonEventArgs e)
    {
        if (_grabbed != Grabbed.Dial) return;
        _grabbed = Grabbed.None;
        Dial.ReleaseMouseCapture();
        TurnDial(e, final: true);
        e.Handled = true;
    }

    void OnDialLost(object sender, MouseEventArgs e)
    {
        if (_grabbed != Grabbed.Dial) return;
        _grabbed = Grabbed.None;
        _model?.EndDrag();
    }

    void TurnDial(MouseEventArgs e, bool final)
    {
        var centre = new Point(Dial.ActualWidth / 2, Dial.ActualHeight / 2);
        var snap = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _model?.SetAngle(GradientEditorViewModel.AngleAt(centre, e.GetPosition(Dial), snap), final);
    }

    void OnAngleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _model?.CommitAngle();
        e.Handled = true;
    }

    void OnAngleLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_model is { } model && model.AngleText != model.Angle.ToString(System.Globalization.CultureInfo.InvariantCulture)) model.CommitAngle();
    }

    // --- The radial preview ---

    Size PreviewBox(GradientEditorViewModel model)
    {
        var scale = PreviewSize / Math.Max(model.Box.Width, model.Box.Height);
        return new Size(Math.Max(24, model.Box.Width * scale), Math.Max(24, model.Box.Height * scale));
    }

    void DrawPreview(GradientEditorViewModel model)
    {
        var size = PreviewBox(model);
        Preview.Width = size.Width;
        Preview.Height = size.Height;
        PreviewFill.Fill = GradientEditorViewModel.BrushOf(model.Gradient);
        var centre = new Point(model.CenterX * size.Width, model.CenterY * size.Height);
        var reach = model.Radius * Math.Max(size.Width, size.Height) / 2;
        WpfCanvas.SetLeft(CenterHandle, centre.X - 5);
        WpfCanvas.SetTop(CenterHandle, centre.Y - 5);
        WpfCanvas.SetLeft(RadiusHandle, centre.X + reach - 4.5);
        WpfCanvas.SetTop(RadiusHandle, centre.Y - 4.5);
        RadiusRing.Width = RadiusRing.Height = Math.Max(0, reach * 2);
        WpfCanvas.SetLeft(RadiusRing, centre.X - reach);
        WpfCanvas.SetTop(RadiusRing, centre.Y - reach);
    }

    void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (_model is not { } model) return;
        var at = e.GetPosition(Preview);
        var size = PreviewBox(model);
        var centre = new Point(model.CenterX * size.Width, model.CenterY * size.Height);
        var radius = new Point(centre.X + model.Radius * Math.Max(size.Width, size.Height) / 2, centre.Y);
        _grabbed = (at - radius).Length <= GrabReach ? Grabbed.Radius : Grabbed.Center;
        Preview.CaptureMouse();
        DragPreview(at, final: false);
        e.Handled = true;
    }

    void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (_grabbed is Grabbed.Center or Grabbed.Radius) DragPreview(e.GetPosition(Preview), final: false);
    }

    void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (_grabbed is not (Grabbed.Center or Grabbed.Radius)) return;
        DragPreview(e.GetPosition(Preview), final: true);
        _grabbed = Grabbed.None;
        Preview.ReleaseMouseCapture();
        e.Handled = true;
    }

    void OnPreviewLost(object sender, MouseEventArgs e)
    {
        if (_grabbed is not (Grabbed.Center or Grabbed.Radius)) return;
        _grabbed = Grabbed.None;
        _model?.EndDrag();
    }

    void DragPreview(Point at, bool final)
    {
        if (_model is not { } model) return;
        var size = PreviewBox(model);
        if (_grabbed == Grabbed.Radius)
        {
            var centre = new Point(model.CenterX * size.Width, model.CenterY * size.Height);
            model.SetRadius(GradientEditorViewModel.RadiusAt(centre, at, size), final);
        }
        else
        {
            var (x, y) = GradientEditorViewModel.CenterAt(at, size);
            model.SetCenter(x, y, final);
        }
    }
}
