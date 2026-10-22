using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Marquee.Editor.Properties;

public partial class RulesView : UserControl
{
    Point _start;
    public RulesView() => InitializeComponent();

    void OnTryValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { IsLoaded: true, DataContext: RulesViewModel rules }
            && Math.Abs(e.NewValue - rules.TryNumber) > 0.000001)
            rules.TryNumber = e.NewValue;
    }

    void OnValueKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ((FrameworkElement)sender).DataContext is not RuleRowViewModel row) return;
        row.CommitCondition();
        e.Handled = true;
    }

    void OnValueLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        (((FrameworkElement)sender).DataContext as RuleRowViewModel)?.CommitCondition();

    void OnDragStart(object sender, MouseButtonEventArgs e) => _start = e.GetPosition(this);

    void OnDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: RuleRowViewModel { IsDraft: false } row } element) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(element, row, DragDropEffects.Move);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is RulesViewModel rules && sender is FrameworkElement { DataContext: RuleRowViewModel target }
            && e.Data.GetData(typeof(RuleRowViewModel)) is RuleRowViewModel source)
        {
            rules.Move(source, target.Index);
            e.Handled = true;
        }
    }
}
