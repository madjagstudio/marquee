using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Layers;

/// Shows a LayersViewModel and passes it the mouse and keys: clicks select (Ctrl and Shift as in Explorer), a
/// double-click or F2 renames, the eye and lock buttons hide and lock, a right-click opens the menu (group, ungroup,
/// copy, paste, duplicate, delete, rename), and a row dragged onto another drops above, below or into it.
public partial class LayersView : UserControl
{
    LayersViewModel? _model;
    LayerItem? _pressed;
    Point _pressedAt;

    public LayersView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            _model = e.NewValue as LayersViewModel;
            WidgetList.ContextMenu = _model is null ? null : Menu(_model);
            BackgroundList.ContextMenu = _model is null ? null : Menu(_model);
        };
    }

    static LayerItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as LayerItem;

    void OnRowDown(object sender, MouseButtonEventArgs e)
    {
        if (_model is null || ItemOf(sender) is not { } item || item.IsRenaming) return;
        Focus();
        if (e.ClickCount == 2)
        {
            _model.BeginRename(item);
            _pressed = null;
            e.Handled = true;
            return;
        }

        _model.Click(item, Keyboard.Modifiers);
        _pressed = item;
        _pressedAt = e.GetPosition(this);
        e.Handled = true;
    }

    void OnRowUp(object sender, MouseButtonEventArgs e) => _pressed = null;

    /// A press that has moved far enough starts dragging the row.
    void OnRowMove(object sender, MouseEventArgs e)
    {
        if (_pressed is not { } item || e.LeftButton != MouseButtonState.Pressed || _model?.CanEdit != true || !item.CanMove) return;
        var moved = e.GetPosition(this) - _pressedAt;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _pressed = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(LayerItem), item), DragDropEffects.Move);
    }

    void OnRowRightDown(object sender, MouseButtonEventArgs e)
    {
        if (_model is null || ItemOf(sender) is not { } item) return;
        _model.ContextClick(item);
        if ((item.IsBackground ? BackgroundList.ContextMenu : WidgetList.ContextMenu) is { } menu) menu.Tag = item;
    }

    void OnRowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (_model is null || ItemOf(sender) is not { } target || e.Data.GetData(typeof(LayerItem)) is not LayerItem dragged) return;
        var row = (FrameworkElement)sender;
        var place = LayersViewModel.PlaceAt(e.GetPosition(row).Y, row.ActualHeight, target.IsGroup);
        var fits = _model.DropFor(dragged, target, place) is not null;
        target.DropHint = fits ? place : null;
        e.Effects = fits ? DragDropEffects.Move : DragDropEffects.None;
    }

    void OnRowDragLeave(object sender, DragEventArgs e)
    {
        if (ItemOf(sender) is { } target) target.DropHint = null;
    }

    void OnRowDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_model is null || ItemOf(sender) is not { } target || e.Data.GetData(typeof(LayerItem)) is not LayerItem dragged) return;
        target.DropHint = null;
        var row = (FrameworkElement)sender;
        _model.Drop(dragged, target, LayersViewModel.PlaceAt(e.GetPosition(row).Y, row.ActualHeight, target.IsGroup));
    }

    void OnHideClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) _model?.SetHidden(item, !item.Hidden);
    }

    void OnLockClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) _model?.SetLocked(item, !item.Locked);
    }

    void OnFoldClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) _model?.ToggleExpanded(item);
    }

    void OnRenameShown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true } box) return;
        // After layout, so the box can take the focus.
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (_model is null || ItemOf(sender) is not { } item) return;
        switch (e.Key)
        {
            case Key.Enter:
                _model.CommitRename(item);
                Focus();
                e.Handled = true;
                break;
            case Key.Escape:
                _model.CancelRename(item);
                Focus();
                e.Handled = true;
                break;
        }
    }

    void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ItemOf(sender) is { IsRenaming: true } item) _model?.CommitRename(item);
    }

    /// F2 renames the selected row.
    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || _model is null || e.OriginalSource is TextBox) return;
        if (_model.Widgets.FirstOrDefault(item => item.IsSelected) is { } selected)
        {
            _model.BeginRename(selected);
            e.Handled = true;
        }
    }

    /// The right-click menu, its commands the panel's own (which the canvas's keys share).
    static ContextMenu Menu(LayersViewModel model)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(Strings.MenuGroup, Strings.GroupTip, "Ctrl+G", model.GroupCommand));
        menu.Items.Add(Item(Strings.MenuUngroup, Strings.UngroupTip, "Ctrl+Shift+G", model.UngroupCommand));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.MenuCopy, Strings.CopyTip, "Ctrl+C", model.CopyCommand));
        menu.Items.Add(Item(Strings.MenuPaste, Strings.PasteTip, "Ctrl+V", model.PasteCommand));
        menu.Items.Add(Item(Strings.MenuDuplicate, Strings.DuplicateTip, "Ctrl+D", model.DuplicateCommand));
        menu.Items.Add(Item(Strings.MenuDelete, Strings.DeleteItemTip, "Del", model.DeleteCommand));
        menu.Items.Add(new Separator());
        var rename = Item(Strings.MenuRename, Strings.RenameLayerTip, "F2", model.RenameCommand);
        menu.Items.Add(rename);
        // The row right-clicked is the menu's Tag (OnRowRightDown): Rename renames it.
        menu.Opened += (_, _) =>
        {
            rename.CommandParameter = menu.Tag;
            model.RefreshCommands();
        };
        return menu;
    }

    static MenuItem Item(string header, string tooltip, string keys, System.Windows.Input.ICommand command) =>
        new() { Header = new TextBlock { Text = header }, ToolTip = tooltip, InputGestureText = keys, Command = command };
}
