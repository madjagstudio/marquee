using System.Windows;
using System.Windows.Controls;
using Marquee.Editor.Model;

namespace Marquee.Editor.Backgrounds;

public partial class BackgroundGalleryView : Window
{
    readonly BackgroundGalleryViewModel _model;
    public BackgroundGalleryView(BackgroundGalleryViewModel model)
    {
        _model = model; DataContext = model; InitializeComponent();
        GalleryPlayback.Attach(this, model.Start, model.Stop);
        Closed += (_, _) => model.Dispose();
    }
    public BackgroundEntry? Chosen { get; private set; }
    public string? Still { get; private set; }
    void OnMoving(object sender, RoutedEventArgs e)
    {
        Chosen = ((LiveThemeTile)((Button)sender).DataContext).Item as BackgroundEntry;
        DialogResult = true;
    }
    void OnStill(object sender, RoutedEventArgs e) { Still = (string)((Button)sender).Tag; DialogResult = true; }
}

/// A gallery may keep its window loaded while hidden, minimised, or with its picture pane collapsed.
internal static class GalleryPlayback
{
    public static void Attach(Window window, Action start, Action stop)
    {
        var pane = (FrameworkElement)window.Content;
        var closed = false;
        void Update()
        {
            if (!closed && window.IsLoaded && window.IsVisible && pane.IsVisible && window.WindowState != WindowState.Minimized) start();
            else stop();
        }
        window.Loaded += (_, _) => Update();
        window.IsVisibleChanged += (_, _) => Update();
        pane.IsVisibleChanged += (_, _) => Update();
        window.StateChanged += (_, _) => Update();
        window.Unloaded += (_, _) => stop();
        window.Closed += (_, _) => { closed = true; stop(); };
    }
}
