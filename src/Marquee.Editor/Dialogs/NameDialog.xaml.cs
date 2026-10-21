using System.Windows;
using System.Windows.Controls;

namespace Marquee.Editor.Dialogs;

/// Asks for a name, checking it as it is typed with `check` (which says why a name can't be used, or null), and
/// allowing OK only for one it takes.
public partial class NameDialog : Window
{
    readonly Func<string, string?> _check;

    public NameDialog(string title, string prompt, string suggested, Func<string, string?> check)
    {
        _check = check;
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        NameBox.Text = suggested;
        NameBox.SelectAll();
    }

    /// The name given, trimmed, once OK is chosen.
    public string ChosenName => NameBox.Text.Trim();

    void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var problem = _check(NameBox.Text);
        Problem.Text = problem ?? "";
        OkButton.IsEnabled = problem is null;
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        if (_check(NameBox.Text) is null) DialogResult = true;
    }
}
