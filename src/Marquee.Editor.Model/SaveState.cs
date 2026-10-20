namespace Marquee.Editor.Model;

public enum SaveStatus
{
    /// layout.yaml holds the session's text.
    Saved,

    /// A change is waiting for autosave, or being written.
    Saving,

    /// The last save failed; the session keeps the text and tries again at RetryAt.
    Failed,
}

/// Why a save failed, so the shell can offer what fits.
public enum SaveProblem
{
    /// Not failed.
    None,

    /// layout.yaml couldn't be written: locked, the disk full, access denied, or a link on the path.
    Write,

    /// The opened version couldn't be copied first (OpenedBackup), so layout.yaml wasn't saved: the session's first
    /// save never goes ahead without the copy that "Revert to opened version" can put back after the editor closes.
    Backup,

    /// The theme's folder, or its layout.yaml, is gone: moved, renamed or deleted while the editor had it open.
    /// Neither is made again in its place. The text is kept for recovery, and the shell offers to save it to a folder
    /// the user chooses (Autosaver.SaveTo). It is still tried again, in case the folder comes back.
    ThemeGone,
}

/// What the toolbar says about saving. Reason, RetryAt and Problem are set when Status is Failed.
public sealed record SaveState(SaveStatus Status, string? Reason = null, DateTimeOffset? RetryAt = null,
    SaveProblem Problem = SaveProblem.None)
{
    public static readonly SaveState Saved = new(SaveStatus.Saved);

    public static readonly SaveState Saving = new(SaveStatus.Saving);

    public static SaveState Failed(string reason, DateTimeOffset retryAt, SaveProblem problem = SaveProblem.Write) =>
        new(SaveStatus.Failed, reason, retryAt, problem);
}
