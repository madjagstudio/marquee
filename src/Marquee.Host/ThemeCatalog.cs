using Marquee.Core.Themes;

namespace Marquee.Host;

/// A theme the tray's Themes menu and the editor's theme picker offer: its name, which is what config.yaml's `theme:`
/// says, and its folder.
public sealed record ThemeEntry(string Name, string Folder);

/// The themes in the theme folders, for the tray's Themes menu and the editor's theme picker, so both offer the same
/// themes the same way.
public static class ThemeCatalog
{
    /// One list per folder, in the folders' order (the user's own themes, then the installed examples), each sorted by
    /// name. A theme is a folder holding a layout.yaml. A name already listed for an earlier folder is left out of the
    /// later ones: config.yaml names themes, and a name finds the earlier folder's (ThemeLocator), so the later one
    /// couldn't be picked by name anyway. A folder that is missing or can't be read lists nothing.
    public static IReadOnlyList<IReadOnlyList<ThemeEntry>> List(IReadOnlyList<string> folders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Windows folder names ignore case
        var groups = new List<IReadOnlyList<ThemeEntry>>();
        foreach (var folder in folders)
        {
            var themes = Themes(folder).Where(theme => !seen.Contains(theme.Name)).ToList();
            seen.UnionWith(themes.Select(theme => theme.Name));
            groups.Add(themes);
        }

        return groups;
    }

    static IEnumerable<ThemeEntry> Themes(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder)
                .Where(theme => File.Exists(Path.Combine(theme, ThemeLoader.LayoutFileName)))
                .Select(theme => new ThemeEntry(Path.GetFileName(theme), Path.GetFullPath(theme)))
                .OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
