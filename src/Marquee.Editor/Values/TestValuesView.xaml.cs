using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Marquee.Editor.Values;

/// Shows a TestValuesViewModel: a click on a key's line opens or closes its pin; Enter, or leaving the box, pins the
/// number typed.
public partial class TestValuesView : UserControl
{
    public TestValuesView() => InitializeComponent();

    static TestValueRowViewModel? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as TestValueRowViewModel;

    void OnLineDown(object sender, MouseButtonEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        row.IsOpen = !row.IsOpen;
        e.Handled = true;
    }

    void OnNumberKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || RowOf(sender) is not { } row) return;
        row.CommitNumber();
        e.Handled = true;
    }

    void OnNumberLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (RowOf(sender) is { } row && row.NumberText != row.Number.ToString(System.Globalization.CultureInfo.CurrentCulture)) row.CommitNumber();
    }
}
