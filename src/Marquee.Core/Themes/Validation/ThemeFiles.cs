using System.Globalization;

namespace Marquee.Core.Themes.Validation;

/// The checks on a theme folder's files that only matter once themes are shared: how large each file is, how large and
/// how many they are together, and that none is a link. A link (a symbolic link or a junction) could reach anywhere on
/// the disk of whoever installs the theme, so it is reported and never followed.
static class ThemeFiles
{
    const double BytesPerMB = 1024 * 1024;

    /// Adds an issue for each problem, and returns the files (as issues name them) that must not be read: links, and
    /// files over the size limit. Stops counting once there are more files than the limit, so a folder of a million
    /// files costs no more than one just over it.
    public static HashSet<string> Check(string folder, ThemeLimits limits, List<ThemeIssue> issues)
    {
        var unsafeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
        var pending = new Stack<DirectoryInfo>([new DirectoryInfo(folder)]);
        var files = 0;
        var total = 0L;
        while (pending.TryPop(out var directory))
        {
            List<FileSystemInfo> entries;
            try
            {
                entries = directory.EnumerateFileSystemInfos("*", options).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(ThemeIssue.Error(Relative(folder, directory.FullName), $"This folder could not be read: {ex.Message}"));
                continue;
            }

            foreach (var entry in entries)
            {
                var name = Relative(folder, entry.FullName);

                if (IsLink(entry))
                {
                    issues.Add(ThemeIssue.Error(name,
                        "This is a symbolic link or junction, which a shared theme can't contain; it was not followed."));
                    unsafeFiles.Add(name);
                    continue;
                }

                if (entry is DirectoryInfo subfolder)
                {
                    pending.Push(subfolder);
                    continue;
                }

                if (++files > limits.MaxFiles)
                {
                    issues.Add(ThemeIssue.Error("", string.Create(CultureInfo.InvariantCulture,
                        $"The theme has more than {limits.MaxFiles:N0} files, the limit for a theme.")));
                    return unsafeFiles;
                }

                var length = ((FileInfo)entry).Length;
                total += length;
                if (length > limits.MaxFileBytes)
                {
                    issues.Add(ThemeIssue.Error(name,
                        $"The file is {Megabytes(length)} MB, over the {Megabytes(limits.MaxFileBytes)} MB limit for a file in a theme."));
                    unsafeFiles.Add(name);
                }
            }
        }

        if (total > limits.MaxTotalBytes)
        {
            issues.Add(ThemeIssue.Error("",
                $"The theme's files add up to {Megabytes(total)} MB, over the {Megabytes(limits.MaxTotalBytes)} MB limit for a theme."));
        }

        return unsafeFiles;
    }

    /// The file a loader's warning is about, as issues name files: relative to the theme folder with forward slashes,
    /// however layout.yaml spelled it. A warning about no file, or about a path that isn't in the folder, is about the
    /// layout.yaml line that names it.
    public static string Display(string folder, string? file)
    {
        if (file is null || !ThemeAssets.TryInsideFolder(folder, file, out var full) || full is null)
            return ThemeLoader.LayoutFileName;
        return Relative(folder, full);
    }

    /// Whether the asset loaders must leave `file` (as issues name files) unopened: it is one of the files Check
    /// refused, or the way to it goes through a link. The way is checked on its own, so a link that Check didn't reach,
    /// having stopped counting at the file limit, isn't followed either.
    public static bool Refused(string folder, string file, HashSet<string> unsafeFiles)
    {
        if (unsafeFiles.Contains(file)) return true;
        var path = folder;
        foreach (var part in file.Split('/'))
        {
            path = Path.Combine(path, part);
            if (!File.Exists(path) && !Directory.Exists(path)) return false; // missing, which the loader reports
            if (IsLink(new FileInfo(path))) return true;
        }

        return false;
    }

    /// LinkTarget is set for symbolic links and junctions only, not for other reparse points such as OneDrive's
    /// placeholders for files kept online, which are ordinary files to everyone reading them. An entry whose link
    /// target can't be read is taken to be a link, so it isn't followed either.
    static bool IsLink(FileSystemInfo entry)
    {
        try
        {
            return entry.LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    static string Relative(string folder, string path) => Path.GetRelativePath(folder, path).Replace('\\', '/');

    /// Rounded up, so a file just over a limit never reads as at it.
    public static string Megabytes(long bytes) => Math.Ceiling(bytes / BytesPerMB).ToString("N0", CultureInfo.InvariantCulture);
}
