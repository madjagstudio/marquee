using Marquee.Core;

namespace Marquee.Editor.Model;

/// Where the editor keeps its own copies of a theme's layout: %LOCALAPPDATA%\Marquee\editor in use, a temp folder in
/// tests. Each theme's files are named by ThemeIdentity.Key, as `<name>-<hash>.yaml`.
public sealed record EditorFolders(string Root)
{
    /// %LOCALAPPDATA%\Marquee\editor. The editor runs unelevated, so its files belong with the user's own.
    public static EditorFolders Default => new(Path.Combine(MarqueePaths.DataRoot, "editor"));

    /// The layouts as they were when they were opened (OpenedBackup).
    public string Opened => Path.Combine(Root, "opened");

    /// The unsaved text of the sessions open now, or that ended without closing (RecoveryStore).
    public string Recovery => Path.Combine(Root, "recovery");

    public string OpenedFile(string themeFolder) => Path.Combine(Opened, FileName(themeFolder));

    public string RecoveryFile(string themeFolder) => Path.Combine(Recovery, FileName(themeFolder));

    static string FileName(string themeFolder) => ThemeIdentity.Key(themeFolder) + ".yaml";
}
