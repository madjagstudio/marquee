using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using WpfCanvas = System.Windows.Controls.Canvas;

namespace Marquee.Editor.Controls;

/// A colour picked in the picker, as layout.yaml writes it ("#RRGGBB", or "#RRGGBBAA" when it isn't opaque).
public sealed class ColorPickedEventArgs(string hex) : EventArgs
{
    public string Hex { get; } = hex;
}

/// The colour picker the properties panel and the gradient editor share, written for the editor (no packages): an HSV
/// square and a hue strip, an alpha strip, a hex box and the recent colours (RecentColors.Shared).
/// - Color is the colour shown, set from outside; the picker keeps its own hue, saturation and value (Hsva), so a
///   colour dragged to grey or black keeps its hue.
/// - ColorChanging is raised as the mouse drags in the square or a strip; ColorCommitted when it is let go, a hex is
///   typed (Enter, or leaving the box) or a recent colour is clicked. A run of ColorChanging is one undo step, closed
///   by the ColorCommitted after it.
public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(string),
        typeof(ColorPicker), new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

    Hsva _hsva = new(0, 0, 1, 1);
    FrameworkElement? _dragging;
    bool _setting;

    public ColorPicker()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RecentColors.Shared.Changed += OnRecentChanged;
            ShowRecent();
            Show();
        };
        Unloaded += (_, _) => RecentColors.Shared.Changed -= OnRecentChanged;
        SizeChanged += (_, _) => Show();
    }

    /// The colour, as layout.yaml writes it.
    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// The hue, saturation, value and alpha the picker shows.
    public Hsva Current => _hsva;

    /// RGB shader uniforms cannot represent transparency.
    public bool ShowAlpha
    {
        get => AlphaStrip.Visibility == Visibility.Visible;
        set => AlphaStrip.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public event EventHandler<ColorPickedEventArgs>? ColorChanging;

    public event EventHandler<ColorPickedEventArgs>? ColorCommitted;

    static void OnColorChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)owner;
        if (picker._setting || !ColorText.TryParse(e.NewValue as string, out var color)) return;
        var next = Hsva.FromColor(color);
        // A grey has no hue of its own, and black no saturation: keep the ones chosen.
        if (next.S == 0 || next.V == 0) next = next with { H = picker._hsva.H };
        if (next.V == 0) next = next with { S = picker._hsva.S };
        picker._hsva = next;
        picker.Show();
    }

    void OnAreaDown(object sender, MouseButtonEventArgs e)
    {
        var area = (FrameworkElement)sender;
        _dragging = area;
        area.CaptureMouse();
        Pick(area, e.GetPosition(area));
        e.Handled = true;
    }

    void OnAreaMove(object sender, MouseEventArgs e)
    {
        if (_dragging is { } area && ReferenceEquals(area, sender)) Pick(area, e.GetPosition(area));
    }

    void OnAreaUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is not { } area || !ReferenceEquals(area, sender)) return;
        _dragging = null;
        area.ReleaseMouseCapture();
        Commit();
        e.Handled = true;
    }

    void OnAreaLost(object sender, MouseEventArgs e)
    {
        if (_dragging is not { } area || !ReferenceEquals(area, sender)) return;
        _dragging = null;
        Commit();
    }

    /// Moves the square's or a strip's thumb to `at`, and the colour with it.
    void Pick(FrameworkElement area, Point at)
    {
        var x = area.ActualWidth > 0 ? Math.Clamp(at.X / area.ActualWidth, 0, 1) : 0;
        var y = area.ActualHeight > 0 ? Math.Clamp(at.Y / area.ActualHeight, 0, 1) : 0;
        _hsva = area == Square ? _hsva with { S = x, V = 1 - y }
            : area == HueStrip ? _hsva with { H = x * 360 }
            : _hsva with { A = x };
        Changed(commit: false);
    }

    void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        TakeHex();
        e.Handled = true;
    }

    void OnHexLostFocus(object sender, KeyboardFocusChangedEventArgs e) => TakeHex();

    void TakeHex()
    {
        if (!ColorText.TryParse(HexBox.Text, out var color))
        {
            HexBox.Text = Color;
            return;
        }

        var text = ColorText.Format(color);
        if (text == Color) return;
        _hsva = Hsva.FromColor(color) is var next && next.S == 0 ? next with { H = _hsva.H } : next;
        Changed(commit: true);
    }

    void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecentSwatch { Hex: var hex } || !ColorText.TryParse(hex, out var color)) return;
        _hsva = Hsva.FromColor(color);
        Changed(commit: true);
    }

    void Changed(bool commit)
    {
        _setting = true;
        try
        {
            Color = ColorText.Format(_hsva.ToColor());
        }
        finally
        {
            _setting = false;
        }

        Show();
        var args = new ColorPickedEventArgs(Color);
        if (commit) ColorCommitted?.Invoke(this, args);
        else ColorChanging?.Invoke(this, args);
    }

    void Commit() => ColorCommitted?.Invoke(this, new ColorPickedEventArgs(Color));

    /// Draws the thumbs, the square's hue, the alpha strip's colour, the swatch and the hex.
    void Show()
    {
        var color = _hsva.ToColor();
        var opaque = MediaColor.FromRgb(color.R, color.G, color.B);
        HueFill.Fill = Frozen(new SolidColorBrush(new Hsva(_hsva.H, 1, 1, 1).ToColor()));
        AlphaFill.Fill = Frozen(new LinearGradientBrush(MediaColor.FromArgb(0, opaque.R, opaque.G, opaque.B), opaque, 0));
        Swatch.Fill = Frozen(new SolidColorBrush(color));
        if (!HexBox.IsKeyboardFocused) HexBox.Text = ColorText.Format(color);

        Place(SquareThumb, _hsva.S * Square.ActualWidth - 6, (1 - _hsva.V) * Square.ActualHeight - 6);
        Place(HueThumb, (_hsva.H % 360 + 360) % 360 / 360 * HueStrip.ActualWidth - 3, 0);
        Place(AlphaThumb, _hsva.A * AlphaStrip.ActualWidth - 3, 0);
    }

    /// The shared list changed, maybe on another thread: tests open windows on STA threads of their own, and a picker
    /// still loaded when its thread ended stays subscribed. It is shown on the picker's own thread, or not at all.
    void OnRecentChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) ShowRecent();
        else Dispatcher.BeginInvoke(ShowRecent);
    }

    void ShowRecent() => Recent.ItemsSource = RecentColors.Shared.Colors.Select(RecentSwatch.Of).ToList();

    static void Place(UIElement element, double left, double top)
    {
        WpfCanvas.SetLeft(element, left);
        WpfCanvas.SetTop(element, top);
    }

    static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// A recent colour's button: its hex, and a brush of it (WPF reads "#AARRGGBB", layout.yaml writes the alpha
    /// last, so the brush is made here rather than from the text).
    sealed record RecentSwatch(string Hex, Brush Brush)
    {
        public static RecentSwatch Of(string hex)
        {
            ColorText.TryParse(hex, out var color);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return new RecentSwatch(hex, brush);
        }
    }
}
