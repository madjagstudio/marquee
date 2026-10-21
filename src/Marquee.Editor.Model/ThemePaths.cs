namespace Marquee.Editor.Model;

/// Files in a theme folder as the import and the clean-up see them: by the forward-slash paths layout.yaml uses, and
/// whether one is hidden. Which reparse points are links is ReparseTags', which lets a OneDrive placeholder through.
static class ThemePaths
{
    /// `full`'s path relative to `root` with forward slashes (`images/logo.png`), or null when it isn't inside it.
    public static string? Relative(string root, string full)
    {
        var inside = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return full.StartsWith(inside, StringComparison.OrdinalIgnoreCase) && full.Length > inside.Length
            ? full[inside.Length..].Replace(Path.DirectorySeparatorChar, '/')
            : null;
    }

    /// The full path `relative` (as layout.yaml writes it, with either slash) names inside `root`, or null when it
    /// leaves the folder or isn't a path at all. The theme loader resolves a theme's paths the same way.
    public static string? Resolve(string root, string relative)
    {
        try
        {
            var full = Path.GetFullPath(Path.Combine(root, relative));
            return Relative(root, full) is null ? null : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// Whether any part of `full` below `root`, the file included, is hidden: a dot name (.git, .layout.yaml.tmp), or
    /// the Hidden or System attribute (desktop.ini, Thumbs.db). The clean-up leaves those alone.
    public static bool Hidden(string root, string full) =>
        Parts(root, full).Any(part => Path.GetFileName(part).StartsWith('.')
            || Attributes(part) is { } attributes && (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0);

    /// The entry's own attributes (a link's, not its target's), or null when it is missing or can't be read.
    public static FileAttributes? Attributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// Each folder from just inside `root` down to `full`, then `full`.
    static IEnumerable<string> Parts(string root, string full)
    {
        var path = Path.TrimEndingDirectorySeparator(root);
        foreach (var part in (Relative(root, full) ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, part);
            yield return path;
        }
    }
}
