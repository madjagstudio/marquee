using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Controls;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using SkiaSharp;

namespace Marquee.Editor.Gradients;

/// One stop on the gradient editor's bar: its colour, where it sits (0 to 1), whether it is the one chosen, and
/// whether a drag is pulling it off the bar to remove it.
public sealed record GradientStopView(int Index, string Color, double Position, bool IsSelected, bool IsLeaving);

/// The gradient editor (spec Part B, Gradient editor), over a GradientModel of one paint:
/// - linear or radial;
/// - a bar of stops: drag one along it (writing `stops`), click the bar to add a stop there in the colour the gradient
///   has at that point, remove the chosen one with Del or by dragging it off the bar; recolour the chosen one;
///   "Spread evenly" removes `stops`;
/// - a linear gradient's angle on a dial, or typed;
/// - a radial gradient's centre and radius, dragged on a small preview of the widget's box.
/// Every change goes through GradientModel.Write, which writes only the keys it changes. A drag (a stop, the dial, the
/// centre or radius, the colour picker) is merged into one undo step, closed when it is let go; a click is a step of
/// its own.
public sealed class GradientEditorViewModel : ObservableObject
{
    /// How far from the bar, in WPF units, a stop must be dragged to come off it.
    public const double TearOff = 24;

    readonly YamlPath _paint;
    readonly Func<EditorCommand, bool> _write;
    readonly Action _seal;
    GradientModel _gradient;
    int _selected;
    int? _dragging;
    bool _leaving;
    string _angleText = "";

    /// `paint` is the path the gradient is written to; `box` the widget's size, for the radial preview's shape. `write`
    /// applies a command as the panel does; `seal` closes a merged run of changes.
    public GradientEditorViewModel(YamlPath paint, GradientModel gradient, SKSize box, Func<EditorCommand, bool> write, Action seal)
    {
        _paint = paint;
        _gradient = gradient;
        Box = box.Width > 0 && box.Height > 0 ? box : new SKSize(100, 100);
        _write = write;
        _seal = seal;
        _angleText = Show(gradient.EffectiveAngle);
    }

    public GradientModel Gradient => _gradient;

    /// The widget's box, whose shape the radial preview has.
    public SKSize Box { get; }

    public bool IsLinear => _gradient.Kind == GradientKind.Linear;

    public bool IsRadial => _gradient.Kind == GradientKind.Radial;

    /// Linear or radial; changing it writes the other kind, keeping the colours and stops.
    public GradientKind Kind
    {
        get => _gradient.Kind;
        set
        {
            if (value != _gradient.Kind) Write(_gradient.WithKind(value), merge: false);
        }
    }

    public IReadOnlyList<GradientStopView> Stops =>
        _gradient.Colors.Select((color, i) => new GradientStopView(i, color, _gradient.EffectiveStops[i], i == _selected,
            _leaving && i == _dragging)).ToList();

    /// The stop chosen: the one recoloured, and removed by Del.
    public int Selected
    {
        get => _selected;
        set
        {
            var index = Math.Clamp(value, 0, _gradient.Colors.Count - 1);
            if (Set(ref _selected, index))
            {
                Raise(nameof(Stops));
                Raise(nameof(SelectedColor));
            }
        }
    }

    public string SelectedColor => _gradient.Colors[_selected];

    /// A stop can go: the gradient has more than the fewest colours it takes.
    public bool CanRemove => _gradient.Colors.Count > GradientModel.MinColors;

    /// The stops sit where `stops` puts them, so "Spread evenly" has something to do.
    public bool HasStops => _gradient.Stops is not null;

    public double Angle => _gradient.EffectiveAngle;

    /// The angle box: typed, and written by CommitAngle.
    public string AngleText
    {
        get => _angleText;
        set => Set(ref _angleText, value);
    }

    public double CenterX => _gradient.EffectiveCenter.X;

    public double CenterY => _gradient.EffectiveCenter.Y;

    public double Radius => _gradient.EffectiveRadius;

    /// Takes the gradient as the file now has it, after any change.
    public void Update(GradientModel gradient)
    {
        if (gradient == _gradient) return;
        _gradient = gradient;
        _selected = Math.Clamp(_selected, 0, gradient.Colors.Count - 1);
        _angleText = Show(gradient.EffectiveAngle);
        Raise(null);
    }

    // --- The stop bar ---

    /// Where on the bar a point `x` across a bar `width` wide is, from 0 to 1.
    public static double PositionAt(double x, double width) => width <= 0 ? 0 : Math.Clamp(x / width, 0, 1);

    /// A drag of stop `index` starts: it becomes the chosen stop.
    public void BeginStopDrag(int index)
    {
        _dragging = index;
        _leaving = false;
        Selected = index;
    }

    /// The stop being dragged is at `position` along the bar, and `away` WPF units above or below it. Past TearOff it
    /// shows as leaving (when a stop can go), and is removed on letting go; brought back, it stays.
    public void DragStop(double position, double away)
    {
        if (_dragging is not { } index) return;
        var leaving = Math.Abs(away) > TearOff && CanRemove;
        if (leaving != _leaving)
        {
            _leaving = leaving;
            Raise(nameof(Stops));
        }

        if (!leaving) Write(_gradient.SetStop(index, Math.Round(Math.Clamp(position, 0, 1), 3)), merge: true);
    }

    /// The drag is let go: a stop pulled off the bar is removed, in the same undo step as the drag.
    public void EndStopDrag()
    {
        if (_dragging is not { } index) return;
        var leaving = _leaving;
        _dragging = null;
        _leaving = false;
        if (leaving && CanRemove) Write(_gradient.RemoveColor(index), merge: true);
        else Raise(nameof(Stops));
        _seal();
    }

    /// A click on the bar at `position`, away from the stops: a stop is added there, in the colour the gradient has
    /// at that point, so the gradient looks the same until it is recoloured.
    public void AddStopAt(double position)
    {
        var at = Math.Round(Math.Clamp(position, 0, 1), 3);
        var stops = _gradient.EffectiveStops;
        var index = 0;
        while (index < stops.Count && stops[index] <= at) index++;
        var color = ColorText.Format(ColorAt(_gradient, at));
        // Placed stops first, so the new one sits where it was clicked rather than the colours spreading again.
        var placed = _gradient.Stops is null ? _gradient.SetStop(0, stops[0]) : _gradient;
        var added = placed.AddColor(color, index).SetStop(index, at);
        if (Write(added, merge: false)) Selected = index;
    }

    /// Del: removes the chosen stop, when one can go.
    public bool RemoveSelected()
    {
        if (!CanRemove) return false;
        return Write(_gradient.RemoveColor(_selected), merge: false);
    }

    /// Recolours stop `index`: `final` false while the colour picker is dragged (merged), true when it is let go or
    /// a colour is typed.
    public void SetColor(int index, string color, bool final)
    {
        Write(_gradient.SetColor(index, color), merge: true);
        if (final)
        {
            RecentColors.Shared.Add(color);
            _seal();
        }
    }

    /// Removes `stops`, so the colours spread evenly.
    public void SpreadEvenly()
    {
        if (HasStops) Write(_gradient.SpreadEvenly(), merge: false);
    }

    // --- Linear: the angle ---

    /// The angle from a dial's `centre` to the point `at`, in the gradient's terms: degrees clockwise from pointing
    /// right, 0 to 360, snapped to 15° when `snap`.
    public static double AngleAt(Point centre, Point at, bool snap)
    {
        var degrees = Math.Atan2(at.Y - centre.Y, at.X - centre.X) * 180 / Math.PI;
        if (snap) degrees = Math.Round(degrees / 15) * 15;
        return Math.Round((degrees % 360 + 360) % 360, 1) % 360;
    }

    /// The dial turned to `degrees`: merged while it is dragged, closed when `final`.
    public void SetAngle(double degrees, bool final)
    {
        if (Math.Abs(degrees - Angle) > 1e-9 || _gradient.Angle is null) Write(_gradient.WithAngle(degrees), merge: true);
        if (final) _seal();
    }

    /// Writes the angle typed in the box; false when it isn't a number.
    public bool CommitAngle()
    {
        if (!double.TryParse(AngleText.Trim().TrimEnd('°'), NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)
            || !double.IsFinite(degrees))
        {
            AngleText = Show(Angle);
            return false;
        }

        return Write(_gradient.WithAngle(degrees), merge: false);
    }

    // --- Radial: the centre and radius ---

    /// The centre a point `at` on the preview of `size` stands for, as fractions of the box.
    public static (double X, double Y) CenterAt(Point at, Size size) =>
        (Math.Round(Math.Clamp(at.X / size.Width, 0, 1), 3), Math.Round(Math.Clamp(at.Y / size.Height, 0, 1), 3));

    /// The radius a point `at` on the preview of `size` stands for, with the centre at `centre` (in the preview's
    /// units): its distance as a fraction of half the box's longer side, as the theme measures a radius.
    public static double RadiusAt(Point centre, Point at, Size size) =>
        Math.Round(Math.Clamp((at - centre).Length / (Math.Max(size.Width, size.Height) / 2), 0, GradientModel.MaxRadius), 3);

    /// A drag of the dial, the centre or the radius was cut short (the mouse taken away): what it wrote stays, as one
    /// undo step.
    public void EndDrag() => _seal();

    public void SetCenter(double x, double y, bool final)
    {
        Write(_gradient.WithCenter(x, y), merge: true);
        if (final) _seal();
    }

    public void SetRadius(double radius, bool final)
    {
        Write(_gradient.WithRadius(radius), merge: true);
        if (final) _seal();
    }

    /// The colour the gradient has at `position`, between the stops either side of it.
    public static Color ColorAt(GradientModel gradient, double position)
    {
        var stops = gradient.EffectiveStops;
        var colors = gradient.Colors.Select(text => ColorText.TryParse(text, out var c) ? c : Colors.Transparent).ToList();
        if (position <= stops[0]) return colors[0];
        for (var i = 1; i < stops.Count; i++)
        {
            if (position > stops[i]) continue;
            var span = stops[i] - stops[i - 1];
            var t = span <= 0 ? 1 : (position - stops[i - 1]) / span;
            return Mix(colors[i - 1], colors[i], t);
        }

        return colors[^1];
    }

    /// A WPF brush that draws the gradient as the theme does, near enough for the editor's swatches and previews.
    public static Brush BrushOf(GradientModel gradient)
    {
        var stops = new GradientStopCollection(gradient.Colors.Select((text, i) =>
            new GradientStop(ColorText.TryParse(text, out var c) ? c : Colors.Transparent, gradient.EffectiveStops[i])));
        Brush brush;
        if (gradient.Kind == GradientKind.Linear)
        {
            var radians = gradient.EffectiveAngle * Math.PI / 180;
            var (dx, dy) = (Math.Cos(radians) / 2, Math.Sin(radians) / 2);
            brush = new LinearGradientBrush(stops, new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
        }
        else
        {
            var (x, y) = gradient.EffectiveCenter;
            brush = new RadialGradientBrush(stops)
            {
                Center = new Point(x, y),
                GradientOrigin = new Point(x, y),
                RadiusX = gradient.EffectiveRadius,
                RadiusY = gradient.EffectiveRadius,
            };
        }

        brush.Freeze();
        return brush;
    }

    /// Writes `next` through the panel; on success it becomes the editor's gradient. A gradient the catalog refuses
    /// (GradientModel.Error) isn't written.
    bool Write(GradientModel next, bool merge)
    {
        if (next.Error is not null || !_write(next.Write(_paint, merge))) return false;
        Update(next);
        Raise(nameof(Stops));
        return true;
    }

    static Color Mix(Color a, Color b, double t) => Color.FromArgb(Lerp(a.A, b.A, t), Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));

    static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * Math.Clamp(t, 0, 1));

    static string Show(double degrees) => Math.Round(degrees, 1).ToString(CultureInfo.InvariantCulture);
}
