using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Marquee.Editor.Controls;

namespace Marquee.Editor.Properties;

/// Shows a PropertiesViewModel, each row in the template for its control (PropertyTemplateSelector), and passes the
/// controls' moments to the rows: Enter or leaving a box writes it and Esc puts it back; a slider writes as it moves,
/// one undo step per drag; the colour picker writes as it drags, one step per drag.
public partial class PropertiesView : UserControl
{
    /// The slider being dragged, whose moves are merged until it is let go.
    Slider? _sliding;

    public PropertiesView() => InitializeComponent();

    static PropertyRowViewModel? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as PropertyRowViewModel;

    void OnUniformKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (sender as FrameworkElement)?.DataContext is not UniformComponentViewModel component) return;
        component.Commit();
        e.Handled = true;
    }

    void OnUniformLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UniformComponentViewModel component) component.Commit();
    }

    void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        switch (e.Key)
        {
            case Key.Enter:
                row.Commit();
                e.Handled = true;
                break;
            case Key.Escape:
                row.Revert();
                e.Handled = true;
                break;
        }
    }

    void OnBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // An editable dropdown's own text box loses the focus to its list while it is open.
        if (sender is ComboBox { IsDropDownOpen: true }) return;
        RowOf(sender)?.Commit();
    }

    void OnDropDownClosed(object? sender, EventArgs e) => RowOf(sender!)?.Commit();

    void OnPairKeyDown(object sender, KeyEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        switch (e.Key)
        {
            case Key.Enter:
                row.CommitPair();
                e.Handled = true;
                break;
            case Key.Escape:
                row.Revert();
                e.Handled = true;
                break;
        }
    }

    void OnPairLostFocus(object sender, KeyboardFocusChangedEventArgs e) => RowOf(sender)?.CommitPair();

    /// A slider moved: by the row's own value coming back (ignored), by a drag (merged), or by a click or key (a step
    /// of its own).
    void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RowOf(sender) is not { } row || Math.Abs(e.NewValue - row.Number) < row.Step / 1000) return;
        row.SlideTo(e.NewValue, final: !ReferenceEquals(_sliding, sender));
    }

    void OnSlideStarted(object sender, DragStartedEventArgs e) => _sliding = sender as Slider;

    void OnSlideCompleted(object sender, DragCompletedEventArgs e)
    {
        _sliding = null;
        RowOf(sender)?.EndSlide();
    }

    void OnColorChanging(object? sender, ColorPickedEventArgs e) => RowOf(sender!)?.PreviewColor(e.Hex);

    void OnColorCommitted(object? sender, ColorPickedEventArgs e) => RowOf(sender!)?.CommitColor(e.Hex);
}

/// True for false and false for true, for a binding that needs the opposite of a flag.
public sealed class Not : IValueConverter
{
    public static Not Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
