using Marquee.Core.Files;
using Marquee.Host.Resources;

namespace Marquee.Host.Updates;

/// The files the updater downloads into %ProgramData%\Marquee\updates (MarqueePaths.Updates): installers, their
/// signatures, partial downloads and msiexec's logs of installing them, all named "Marquee-…". Nothing else is
/// Marquee's to delete there.
public static class UpdateDownloads
{
    /// Deletes the downloads in `folder` and then the folder, unless something else is in it; the uninstaller runs this
    /// (as SYSTEM) through `marquee task remove --updates`. Returns how many files it deleted. Throws
    /// UnsafePathException, deleting nothing, when the folder or the one above it is a junction or symbolic link: a
    /// program could have planted one to point SYSTEM's deletes somewhere else.
    public static int Remove(string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (IsLink(folder) || (Path.GetDirectoryName(folder) is { } parent && IsLink(parent)))
            throw new UnsafePathException(Strings.UpdatesFolderIsLink(folder));
        if (!Directory.Exists(folder)) return 0;

        var deleted = DeleteFiles(folder);
        try
        {
            Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not empty: what else is there isn't Marquee's.
        }

        return deleted;
    }

    /// Deletes the files whose names start with "Marquee-" and returns how many. A file that can't be deleted (an
    /// installer a VerifiedUpdate still holds) is left.
    internal static int DeleteFiles(string folder)
    {
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "Marquee-*"))
        {
            // The pattern also matches short (8.3) names; the long name decides.
            if (!Path.GetFileName(file).StartsWith("Marquee-", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left for next time.
            }
        }

        return deleted;
    }

    static bool IsLink(string path)
    {
        try
        {
            // The entry's own attributes: a junction's, not its target's.
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }
}
