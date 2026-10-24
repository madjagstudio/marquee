using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Marquee.Editor.Backgrounds;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Modules;

public partial class ModuleGalleryView : Window
{
    readonly Action<Module> _add;
    Point _pressed;
    public ModuleGalleryView(ModuleGalleryViewModel model, Action<Module> add)
    {
        _add = add; DataContext = model; InitializeComponent();
        GalleryPlayback.Attach(this, model.Start, model.Stop);
        Closed += (_, _) => model.Dispose();
    }
    void OnAdd(object sender, RoutedEventArgs e) { _add((Module)((LiveThemeTile)((Button)sender).DataContext).Item!); Close(); }
    void OnBlank(object sender, RoutedEventArgs e) => Close();
    void OnModuleMenu(object sender, ContextMenuEventArgs e)
    {
        var button = (Button)sender;
        var module = (Module)((LiveThemeTile)button.DataContext).Item!;
        if (module.Category != ModuleCategory.MyModules) { e.Handled = true; return; }
        var model = (ModuleGalleryViewModel)DataContext;
        var menu = button.ContextMenu; menu.Items.Clear();
        var rename = new MenuItem { Header = Strings.RenameModule, ToolTip = Strings.RenameModule };
        rename.Click += (_, _) => Manage(() => { if (model.Dialogs?.AskModuleName(module.Name, model.NameProblem) is { } name) model.Rename(module.Id, name); });
        var delete = new MenuItem { Header = Strings.DeleteModule, ToolTip = Strings.DeleteModule };
        delete.Click += (_, _) => Manage(() => model.Delete(module.Id));
        menu.Items.Add(rename); menu.Items.Add(delete);
        void Manage(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { model.Dialogs?.ShowError(Strings.ImportRefused(module.Name, ex.Message)); }
        }
    }
    void OnPress(object sender, MouseButtonEventArgs e) => _pressed = e.GetPosition(this);
    void OnMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - _pressed;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var module = (Module)((LiveThemeTile)((Button)sender).DataContext).Item!;
        if (DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(Module), module), DragDropEffects.Copy) == DragDropEffects.Copy) Close();
    }
}
