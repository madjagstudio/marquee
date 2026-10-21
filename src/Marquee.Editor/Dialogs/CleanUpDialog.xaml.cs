using System.Globalization;
using System.Windows;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Dialogs;

/// "Clean up unused files": the list to tick (CleanUpViewModel), and a last question before anything is deleted, as
/// deleting can't be undone.
public partial class CleanUpDialog : Window
{
    readonly CleanUpViewModel _model;

    public CleanUpDialog(string theme, CleanUpViewModel model)
    {
        _model = model;
        DataContext = model;
        InitializeComponent();
        Intro.Text = Strings.CleanUpIntro(theme);
    }

    void OnDelete(object sender, RoutedEventArgs e)
    {
        var count = _model.Chosen.Count;
        if (count == 0) return;
        var question = Strings.CleanUpConfirm(count.ToString(CultureInfo.CurrentCulture));
        if (MessageBox.Show(this, question, Strings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            DialogResult = true;
    }
}
