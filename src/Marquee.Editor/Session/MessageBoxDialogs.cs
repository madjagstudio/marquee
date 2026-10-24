using System.Globalization;
using System.Windows;
using Marquee.Core.Themes;
using Marquee.Editor.Dialogs;
using Marquee.Editor.Model;
using Marquee.Editor.Properties;
using Marquee.Editor.Resources;
using Microsoft.Win32;

namespace Marquee.Editor.Session;

/// The dialogs as message boxes and Windows' file dialogs, over the main window once there is one; the copy's name and
/// the clean-up as windows of their own (NameDialog, CleanUpDialog).
public sealed class MessageBoxDialogs : IEditorDialogs
{
    public string? PickOpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = Strings.OpenYourTheme };
        return ShowDialog(dialog) ? dialog.FolderName : null;
    }
    public string? AskStyleName(string suggested, Func<string, string?> problem)
    {
        var dialog = new NameDialog(Strings.SaveAsStyle, Strings.StyleNamePrompt, suggested, problem) { Owner = Owner() };
        return dialog.ShowDialog() == true ? dialog.ChosenName : null;
    }

    public bool? AskRearrange(string orientation)
    {
        var dialog = new OrientationDialog(orientation) { Owner = Owner() };
        dialog.ShowDialog();
        return dialog.Rearrange;
    }

    public string? PickLayerFile(LayerKindChoice kind) => PickImportFile(kind switch
    {
        LayerKindChoice.Image => AssetKind.Image,
        LayerKindChoice.Video => AssetKind.Video,
        LayerKindChoice.Shader => AssetKind.Shader,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    });

    public void ShowError(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInfo(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Information);

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
        return ShowDialog(dialog) ? dialog.FolderName : null;
    }

    public string? PickImportFile(AssetKind kind)
    {
        var (title, filter) = kind switch
        {
            AssetKind.Image => (Strings.ImportImageTitle, Filter(Strings.ImageFiles, PropertyChoices.ImageExtensions)),
            AssetKind.Font => (Strings.ImportFontTitle, Filter(Strings.FontFiles, AssetChecks.FontExtensions)),
            AssetKind.Shader => (Strings.ImportShaderTitle, Filter(Strings.ShaderFiles, [".sksl"])),
            _ => (Strings.ImportVideoTitle, Filter(Strings.VideoFiles, AssetChecks.VideoExtensions)),
        };
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    public string? AskCopyName(string example, string suggested, Func<string, string?> check)
    {
        var dialog = new NameDialog(Strings.CopyNameTitle, Strings.CopyNamePrompt(example), suggested, check) { Owner = Owner() };
        return dialog.ShowDialog() == true ? dialog.ChosenName : null;
    }

    public IReadOnlyList<string>? ChooseUnusedFiles(string theme, IReadOnlyList<UnusedFile> files)
    {
        var model = new CleanUpViewModel(files);
        var dialog = new CleanUpDialog(theme, model) { Owner = Owner() };
        return dialog.ShowDialog() == true ? model.Chosen : null;
    }

    public bool AskRevert(string theme) =>
        Show(Strings.RevertQuestion(theme), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// A time as the user reads times: their own culture, in local time.
    static string When(DateTimeOffset time) => time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// A file dialog's filter: the kind's files, then all files.
    static string Filter(string name, IEnumerable<string> extensions)
    {
        var patterns = string.Join(";", extensions.Select(extension => "*" + extension));
        return $"{name} ({patterns})|{patterns}|{Strings.AllFiles} (*.*)|*.*";
    }

    static bool ShowDialog(CommonDialog dialog) => (Owner() is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    static Window? Owner() => Application.Current?.MainWindow is { IsVisible: true } owner ? owner : null;

    static MessageBoxResult Show(string text, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner() is { } owner
            ? MessageBox.Show(owner, text, Strings.AppName, buttons, image)
            : MessageBox.Show(text, Strings.AppName, buttons, image);
}
