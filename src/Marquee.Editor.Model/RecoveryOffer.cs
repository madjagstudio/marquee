namespace Marquee.Editor.Model;

/// Unsaved changes a session left behind when it ended without closing (a crash, Task Manager), different from
/// layout.yaml now: "Restore unsaved changes", with when each was last written, so the user can tell which is newer.
public sealed record RecoveryOffer(string Text, DateTimeOffset Saved, DateTimeOffset LayoutSaved)
{
    public const string RestoreLabel = "Restore unsaved changes";

    /// The command that makes the recovered text the session's, as one undo step; autosave then saves it.
    public ReplaceDocument Restore() => new(Text, RestoreLabel);
}
