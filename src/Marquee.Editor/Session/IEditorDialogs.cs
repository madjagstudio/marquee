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

/// The questions and messages the shell puts to the user, behind an interface so tests answer them. Kept simple:
/// message boxes in this task (MessageBoxDialogs); Task 12 gives some of them windows of their own.
public interface IEditorDialogs
{
    void ShowError(string message);

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
}
