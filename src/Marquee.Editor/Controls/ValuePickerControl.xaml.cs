using System.Windows.Controls;
using System.Windows.Input;
using Marquee.Editor.Properties;

namespace Marquee.Editor.Controls;

public partial class ValuePickerControl : UserControl
{
    public ValuePickerControl() => InitializeComponent();

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ReferenceEquals(e.OriginalSource, SearchBox)) return;
        if (DataContext is ValuePickerViewModel picker) picker.Commit();
        e.Handled = true;
    }

    void OnValueLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is ValuePickerViewModel picker) picker.Commit();
    }
}
