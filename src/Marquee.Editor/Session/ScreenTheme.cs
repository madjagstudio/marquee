using Marquee.Core.Config;
using Marquee.Editor.Model;
using Marquee.Host;

namespace Marquee.Editor.Session;

/// "Show on screen": writes the theme into config.yaml's `theme:` (ConfigEditor, which changes only that value), and
/// the tray, which follows config.yaml, switches to it.
public sealed class ScreenTheme(string configFile, ThemeLocator locator)
{
    /// Makes `themeFolder` the theme config.yaml names, making the file (and its folder) when there is none yet. Throws
    /// ConfigException for a config.yaml that doesn't parse or has no place for the key, and IOException or
    /// UnauthorizedAccessException when it can't be written; the file is as it was.
    public void Show(string themeFolder) => ConfigEditor.SetInFile(configFile, "theme", ValueFor(themeFolder));

    /// What `theme:` says for `themeFolder`: its name when the name finds this folder, read as the tray reads
    /// config.yaml (from its folder), so the file stays as a person would write it; otherwise its full path.
    public string ValueFor(string themeFolder)
    {
        var folder = ThemeIdentity.FullPath(themeFolder);
        var name = Path.GetFileName(folder);
        var fromConfig = locator.RelativeTo(Path.GetDirectoryName(Path.GetFullPath(configFile))!);
        return fromConfig.TryLocate(name, out var found, out _)
               && string.Equals(ThemeIdentity.FullPath(found), folder, StringComparison.OrdinalIgnoreCase)
            ? name
            : folder;
    }
}
