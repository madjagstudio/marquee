using Marquee.Editor.Model;

namespace Marquee.Editor.Session;

/// What to do with a theme the editor can't save (ThemeAccess.ReadOnly).
public enum CopyChoice
{
    /// Copy it to the user's themes and edit the copy.
    MakeCopy,

    /// Open it to look at, with editing off.
    ViewOnly,

    /// Don't open it.
    Cancel,
}

/// The questions and messages the shell puts to the user, behind an interface so tests answer them. MessageBoxDialogs
/// asks most of them in message boxes and the file dialogs; the copy's name and the clean-up have windows of their own
/// (the Dialogs folder).
public interface IEditorDialogs
{
    string? AskModuleName(string suggested, Func<string, string?> problem) => null;
    string? PickOpenFolder() => null;
    /// Asks for a named style, checking each edit; null when cancelled.
    string? AskStyleName(string suggested, Func<string, string?> problem);

    bool? AskRearrange(string orientation);

    string? PickLayerFile(LayerKindChoice kind);

    void ShowError(string message);

    /// A message that needs no answer, such as what a clean-up did.
    void ShowInfo(string message);

    /// The theme `theme` can't be saved, for `reason`: make a copy to edit, view it read-only, or don't open it.
    CopyChoice AskMakeCopy(string theme, string reason);

    /// A session that ended without closing left unsaved changes to `theme`: restore them?
    bool AskRestore(string theme, RecoveryOffer offer);

    /// `theme` is open in another editor window, which couldn't be brought forward.
    void ShowAlreadyOpen(string theme);

    /// The window is closing but layout.yaml couldn't be saved, for `reason`: close anyway, leaving the changes to be
    /// offered next time?
    bool AskCloseUnsaved(string reason);

    /// "Save to a folder…": `folder` already has a layout.yaml, another theme's perhaps. Replace it?
    bool AskReplaceLayout(string folder);

    /// Asks for a folder to save the theme's text in, its own folder having gone (SaveProblem.ThemeGone); null when the
    /// user cancels.
    string? PickSaveFolder();

    /// Asks for a file to import as a `kind` (an image, a font or a clip); null when the user cancels.
    string? PickImportFile(AssetKind kind);

    /// "New from an example": asks what to call the copy of `example`, starting from `suggested`. `check` says why a
    /// name typed can't be used (ThemeOpening.CopyNameProblem), or null when it can. Null when the user cancels.
    string? AskCopyName(string example, string suggested, Func<string, string?> check);

    /// "Clean up unused files": shows the files of `theme` that nothing uses, to tick and confirm. The paths ticked, or
    /// null when the user cancels.
    IReadOnlyList<string>? ChooseUnusedFiles(string theme, IReadOnlyList<UnusedFile> files);

    /// "Revert to opened version": put `theme` back as it was when the editor opened it, discarding the changes made
    /// since (undo brings them back)?
    bool AskRevert(string theme);
}
