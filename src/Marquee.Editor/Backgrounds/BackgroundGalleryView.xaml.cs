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
        Loaded += (_, _) => model.Start();
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
