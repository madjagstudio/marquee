using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Marquee.Editor.Backgrounds;
using Marquee.Editor.Model;

namespace Marquee.Editor.Modules;

public partial class ModuleGalleryView : Window
{
    readonly Action<Module> _add;
    Point _pressed;
    public ModuleGalleryView(ModuleGalleryViewModel model, Action<Module> add)
    {
        _add = add; DataContext = model; InitializeComponent();
        Loaded += (_, _) => model.Start(); Closed += (_, _) => model.Dispose();
    }
    void OnAdd(object sender, RoutedEventArgs e) { _add((Module)((LiveThemeTile)((Button)sender).DataContext).Item!); Close(); }
    void OnBlank(object sender, RoutedEventArgs e) => Close();
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
