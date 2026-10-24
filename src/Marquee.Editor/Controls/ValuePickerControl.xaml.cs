using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Marquee.Editor.Properties;

namespace Marquee.Editor.Controls;

public partial class ValuePickerControl : UserControl
{
    public ValuePickerControl() => InitializeComponent();

    void OnGroupLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup group } expander && DataContext is ValuePickerViewModel picker)
            expander.SetBinding(Expander.IsExpandedProperty, new Binding("IsExpanded")
            {
                Source = picker.ExpansionFor("sensor:" + group.Name), Mode = BindingMode.TwoWay,
            });
    }

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
