using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Values;

/// Shows a TestValuesViewModel: a click on a key's line opens or closes its pin; Enter, or leaving the box, pins the
/// number typed.
public partial class TestValuesView : UserControl
{
    public TestValuesView() => InitializeComponent();

    // These buttons live inside WPF's system templates, so keep those templates and name their parts here.
    void OnDatePickerLoaded(object sender, RoutedEventArgs e)
    {
        var picker = (DatePicker)sender;
        SetPartTip(picker, "PART_TextBox", Strings.TestDateTip);
        SetPartTip(picker, "PART_Button", Strings.OpenCalendarTip);
    }

    void OnCalendarItemLoaded(object sender, RoutedEventArgs e)
    {
        var calendar = (Control)sender;
        SetPartTip(calendar, "PART_PreviousButton", Strings.PreviousPeriodTip);
        SetPartTip(calendar, "PART_NextButton", Strings.NextPeriodTip);
        SetPartTip(calendar, "PART_HeaderButton", Strings.CalendarViewTip);
    }

    static void SetPartTip(Control owner, string name, string tip)
    {
        owner.ApplyTemplate();
        if (owner.Template.FindName(name, owner) is FrameworkElement part) part.ToolTip = tip;
    }

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
