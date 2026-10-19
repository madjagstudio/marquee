using System.Diagnostics.CodeAnalysis;
using Marquee.Core;
using Marquee.Host.Resources;

namespace Marquee.Host;

/// Turns a theme as the user wrote it, on the command line or as config.yaml's `theme:`, into a folder. A value that is
/// rooted, has a directory separator in it, or is "." or "..", is a path, relative to `currentDirectory`. Anything
/// else is a name, looked up in each of `themeFolders` in order, then as a folder in `currentDirectory`, so that
/// `marquee run example-agents` works from a source checkout as well as from an install. For config.yaml's `theme:`,
/// SettingsResolver makes the config file's folder the current directory (RelativeTo).
public sealed class ThemeLocator(IReadOnlyList<string> themeFolders, string currentDirectory)
{
    /// The user's own themes (%LOCALAPPDATA%\Marquee\themes), then the examples the installer puts in a `themes`
    /// folder beside the exe.
    public static ThemeLocator Default =>
        new([MarqueePaths.Themes, Path.Combine(AppContext.BaseDirectory, "themes")], Environment.CurrentDirectory);

    public IReadOnlyList<string> ThemeFolders => themeFolders;

    public string CurrentDirectory => currentDirectory;

    /// The same theme folders, with `folder` in place of the current directory.
    public ThemeLocator RelativeTo(string folder) => new(themeFolders, folder);

    /// The theme's folder as a full path, or a message saying where it looked.
    public bool TryLocate(string theme, [NotNullWhen(true)] out string? folder, [NotNullWhen(false)] out string? error)
    {
        if (IsPath(theme))
        {
            folder = FullPath(currentDirectory, theme);
            if (folder is not null && Directory.Exists(folder))
            {
                error = null;
                return true;
            }

            error = Strings.ThemeFolderNotFound(folder ?? theme);
            folder = null;
            return false;
        }

        string[] places = [.. themeFolders, currentDirectory];
        foreach (var place in places)
        {
            folder = FullPath(place, theme);
            if (folder is not null && Directory.Exists(folder))
            {
                error = null;
                return true;
            }
        }

        folder = null;
        error = Strings.ThemeNotFound(theme, string.Join("; ", places));
        return false;
    }

    static bool IsPath(string theme) =>
        Path.IsPathRooted(theme)
        || theme.Contains(Path.DirectorySeparatorChar)
        || theme.Contains(Path.AltDirectorySeparatorChar)
        || theme is "." or "..";

    /// Null when Windows can't make a path of it (a character a path can't hold, say): then there is no such folder.
    static string? FullPath(string folder, string relative)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(relative, folder));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
