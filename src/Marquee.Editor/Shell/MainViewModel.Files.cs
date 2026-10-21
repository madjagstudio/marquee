using System.Globalization;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Properties;
using Marquee.Editor.Resources;
using Marquee.Editor.Session;

namespace Marquee.Editor.Shell;

/// The toolbar's file commands: "Add image…" and "Add video…", which import a file into the theme and add a widget
/// showing it (AssetImport); "Clean up unused files…" (UnusedFiles) and "Revert to opened version…" in the theme menu;
/// and "New from an example…", which asks for the copy's name.
public sealed partial class MainViewModel
{
    public RelayCommand AddImageCommand { get; private set; } = null!;

    public RelayCommand AddVideoCommand { get; private set; } = null!;

    public RelayCommand CleanUpCommand { get; private set; } = null!;

    /// The theme menu's actions on the open theme, after the themes and the examples: clean up its unused files, and put
    /// it back as it was opened (asked first; the banner offers it too while paused, and "Keep editing" doesn't hide this).
    public IReadOnlyList<MenuChoice> ThemeActions() =>
    [
        new(Strings.CleanUpMenu, CleanUpCommand),
        new(Strings.RevertMenu, RevertCommand),
    ];

    /// Add widget's file choices, after the widget types: an image or a video from a file.
    public IReadOnlyList<MenuChoice> FileChoices() =>
    [
        new(Strings.AddImage, AddImageCommand),
        new(Strings.AddVideo, AddVideoCommand),
    ];

    void InitializeFileCommands()
    {
        AddImageCommand = new RelayCommand(() => AddFile(AssetKind.Image), () => CanEdit);
        AddVideoCommand = new RelayCommand(() => AddFile(AssetKind.Video), () => CanEdit);
        CleanUpCommand = new RelayCommand(CleanUp, () => CanEdit);
    }

    void RefreshFileCommands()
    {
        AddImageCommand.Refresh();
        AddVideoCommand.Refresh();
        CleanUpCommand.Refresh();
    }

    /// "Add image…" or "Add video…": asks for the file, copies it into the theme (checked as the theme loader would load
    /// it), and adds a widget showing it, in the middle of the canvas or of the group being worked in, as one undo step.
    /// A refusal says why in a message; a clip too slow for this PC is added, with its warning in the status bar.
    public void AddFile(AssetKind kind)
    {
        if (_workspace is not { } workspace || !CanEdit || _environment.Dialogs.PickImportFile(kind) is not { } file) return;
        var session = workspace.Session;
        try
        {
            var import = new AssetImport(session.Folder);
            var imported = kind == AssetKind.Video ? import.AddVideo(session, file, Canvas.Group) : import.AddImage(session, file, Canvas.Group);
            PropertyChoices.Forget();
            Message = imported.Warning;
        }
        catch (AssetImportException ex)
        {
            _environment.Dialogs.ShowError(Strings.ImportRefused(Path.GetFileName(file), ex.Reason));
        }
        catch (Exception ex) when (ex is LayoutEditException or ThemeException or ArgumentException or InvalidOperationException
                                       or IOException or UnauthorizedAccessException)
        {
            _environment.Dialogs.ShowError(Strings.ImportRefused(Path.GetFileName(file), ex.Message));
        }
    }

    /// "Clean up unused files…": lists the files in the theme folder that nothing in the text names, deletes those the
    /// user ticks and confirms, and says what was kept and why.
    public void CleanUp()
    {
        if (_workspace is not { } workspace || !CanEdit) return;
        var dialogs = _environment.Dialogs;
        IReadOnlyList<UnusedFile> unused;
        try
        {
            unused = UnusedFiles.List(workspace.Session);
        }
        catch (Exception ex) when (ex is LayoutEditException or IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError(Strings.CleanUpFailed(ex.Message));
            return;
        }

        if (unused.Count == 0)
        {
            dialogs.ShowInfo(Strings.NoUnusedFiles);
            return;
        }

        if (dialogs.ChooseUnusedFiles(workspace.Name, unused) is not { Count: > 0 } chosen) return;
        IReadOnlyList<FileDeletion> results;
        try
        {
            results = UnusedFiles.Delete(workspace.Session, chosen);
        }
        catch (LayoutEditException ex)
        {
            dialogs.ShowError(Strings.CleanUpFailed(ex.Message));
            return;
        }

        PropertyChoices.Forget();
        var deleted = results.Count(result => result.Deleted);
        Report(Strings.FilesDeleted(deleted.ToString(CultureInfo.CurrentCulture)));
        var kept = results.Where(result => !result.Deleted).Select(result => Strings.FileKept(result.File, result.Refusal!)).ToList();
        if (kept.Count > 0) dialogs.ShowError(Strings.FilesKept(string.Join(Environment.NewLine, kept)));
    }

    /// "New from an example…": asks what to call the copy, copies the example into the user's themes under that name,
    /// and opens it.
    public void NewFromExample(string example)
    {
        var opening = _environment.Opening;
        var suggested = Path.GetFileName(opening.CopyFolderFor(example));
        if (_environment.Dialogs.AskCopyName(Path.GetFileName(ThemeIdentity.FullPath(example)), suggested, opening.CopyNameProblem) is not { } name)
            return;
        try
        {
            OpenTheme(opening.MakeCopy(example, name));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _environment.Dialogs.ShowError(Strings.CouldNotCopy(Path.GetFileName(example), ex.Message));
        }
    }
}
