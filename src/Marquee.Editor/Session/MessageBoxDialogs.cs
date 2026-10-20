using System.Globalization;
using System.Windows;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;
using Microsoft.Win32;

namespace Marquee.Editor.Session;

/// The dialogs as message boxes, over the main window once there is one.
public sealed class MessageBoxDialogs : IEditorDialogs
{
    public void ShowError(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Warning);

    public CopyChoice AskMakeCopy(string theme, string reason) =>
        Show(Strings.MakeCopyQuestion(theme, reason), MessageBoxButton.YesNoCancel, MessageBoxImage.Information) switch
        {
            MessageBoxResult.Yes => CopyChoice.MakeCopy,
            MessageBoxResult.No => CopyChoice.ViewOnly,
            _ => CopyChoice.Cancel,
        };

    public bool AskRestore(string theme, RecoveryOffer offer) =>
        Show(Strings.RestoreQuestion(theme, When(offer.Saved), When(offer.LayoutSaved)), MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowAlreadyOpen(string theme) => Show(Strings.AlreadyOpen(theme), MessageBoxButton.OK, MessageBoxImage.Information);

    public bool AskCloseUnsaved(string reason) =>
        Show(Strings.CloseUnsavedQuestion(reason), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public bool AskReplaceLayout(string folder) =>
        Show(Strings.ReplaceLayoutQuestion(folder), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public string? PickSaveFolder()
    {
        var dialog = new OpenFolderDialog { Title = Strings.SaveToFolderTitle };
        var owner = Application.Current?.MainWindow;
        return (owner is { IsVisible: true } ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true ? dialog.FolderName : null;
    }

    /// A time as the user reads times: their own culture, in local time.
    static string When(DateTimeOffset time) => time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    static MessageBoxResult Show(string text, MessageBoxButton buttons, MessageBoxImage image) =>
        Application.Current?.MainWindow is { IsVisible: true } owner
            ? MessageBox.Show(owner, text, Strings.AppName, buttons, image)
            : MessageBox.Show(text, Strings.AppName, buttons, image);
}
