using System.Windows;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Dialogs;

public partial class OrientationDialog : Window
{
    public OrientationDialog(string orientation)
    {
        InitializeComponent();
        Prompt.Text = Strings.RearrangeQuestion(orientation);
    }

    public bool? Rearrange { get; private set; }
    void OnYes(object sender, RoutedEventArgs e) { Rearrange = true; DialogResult = true; }
    void OnNo(object sender, RoutedEventArgs e) { Rearrange = false; DialogResult = true; }
}
