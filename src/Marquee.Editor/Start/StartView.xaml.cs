using System.Windows;
using System.Windows.Controls;
using Marquee.Editor.Backgrounds;

namespace Marquee.Editor.Start;

public partial class StartView : Window
{
    readonly StartViewModel _model;
    public StartView(StartViewModel model)
    {
        _model = model; DataContext = model; InitializeComponent();
        GalleryPlayback.Attach(this, model.Start, model.Stop);
        Closed += (_, _) => model.Dispose();
    }
    void OnChoose(object sender, RoutedEventArgs e)
    {
        if (_model.Choose((StartChoice)((LiveThemeTile)((Button)sender).DataContext).Item!)) DialogResult = true;
    }
    void OnOpen(object sender, RoutedEventArgs e) { if (_model.OpenYours()) DialogResult = true; }
}
