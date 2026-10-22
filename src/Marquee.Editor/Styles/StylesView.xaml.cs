using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Styles;

public partial class StylesView : UserControl
{
    public StylesView() => InitializeComponent();
    StylesViewModel? Model => DataContext as StylesViewModel;

    void OnRowDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StyleItem item } || Model is not { } model) return;
        if (item.IsRenaming) return;
        Focus();
        model.Select(item.Name);
        if (e.ClickCount == 2) model.BeginRename(item);
        e.Handled = true;
    }

    void OnRowRightDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StyleItem item } row || Model is not { } model) return;
        model.Select(item.Name);
        row.ContextMenu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = Strings.MenuRename, ToolTip = Strings.RenameStyleTip, InputGestureText = "F2", Command = model.RenameCommand },
                new MenuItem { Header = Strings.MenuDelete, ToolTip = Strings.DeleteStyleTip, InputGestureText = "Del", Command = model.DeleteCommand },
            },
        };
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || Model is not { } model || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.F2) model.RenameCommand.Execute(null);
        else if (e.Key == Key.Delete) model.DeleteCommand.Execute(null);
        else return;
        e.Handled = true;
    }

    void OnRenameShown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || !box.IsVisible) return;
        box.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!box.IsVisible) return;
            box.Focus();
            box.SelectAll();
        }));
    }

    void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: StyleItem item } || Model is not { } model) return;
        if (e.Key == Key.Enter) model.CommitRename(item);
        else if (e.Key == Key.Escape) model.CancelRename(item);
        else return;
        if (!item.IsRenaming) Focus();
        e.Handled = true;
    }

    void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: StyleItem item }) Model?.CancelRename(item);
    }
}
