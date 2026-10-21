using Marquee.Core;
using Marquee.Host;

namespace Marquee.App;

/// Where the tray reads and writes: config.yaml, the theme folders, its log folder and its data folder; and the theme
/// editor it starts. Real ones come from MarqueePaths; tests use temp folders.
sealed record TrayPaths(string ConfigFile, ThemeLocator Locator, string Logs, string DataFolder)
{
    /// The user's own themes (%LOCALAPPDATA%\Marquee\themes), the first folder names are looked up in.
    public string UserThemes => Locator.ThemeFolders[0];

    /// MarqueeEditor.exe, which the installer puts beside MarqueeTray.exe.
    public string EditorExe { get; init; } = ThemeEditor.BesideThisExe;

    /// The real places. Reading ProcessLogs secures an elevated tray's folder under %ProgramData% first, and throws
    /// when it can't (Program shows that and stops), so this is called only after it has succeeded once.
    public static TrayPaths Current() =>
        new(MarqueePaths.ConfigFile, ThemeLocator.Default, MarqueePaths.ProcessLogs, MarqueePaths.ProcessDataFolder);
}
