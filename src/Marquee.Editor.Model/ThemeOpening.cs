using System.Globalization;
using System.Text.RegularExpressions;
using Marquee.Core;
using Marquee.Core.Themes;

namespace Marquee.Editor.Model;

/// What happens before a theme opens for editing: whether it can be saved (Check), a copy to edit when it can't
/// (MakeCopy), and one editor per theme folder (TryLock).
///
/// `installedThemes` is where the installer puts the examples (a `themes` folder beside the exe, in Program Files),
/// which updates replace, so they are never edited in place even by an administrator. `userThemes` is
/// %LOCALAPPDATA%\Marquee\themes, where copies go. Both are given, so tests use temp folders.
public sealed partial class ThemeOpening
{
    readonly string _installedThemes;
    readonly string _userThemes;
    readonly ReparseTags _tags;

    public ThemeOpening(string installedThemes, string userThemes)
        : this(installedThemes, userThemes, ReparseTags.Windows)
    {
    }

    /// `tags` is ReparseTags.Windows except in tests.
    internal ThemeOpening(string installedThemes, string userThemes, ReparseTags tags)
    {
        _installedThemes = installedThemes;
        _userThemes = userThemes;
        _tags = tags;
    }

    public static ThemeOpening Default => new(Path.Combine(AppContext.BaseDirectory, "themes"), MarqueePaths.Themes);

    /// Whether the editor can save the theme in `themeFolder`, and if not, why:
    /// - an example installed with Marquee;
    /// - a folder reached through a junction, a symbolic link, a mount point or another reparse point that may lead
    ///   somewhere else, or a layout.yaml that is one (ReparseTags). OneDrive's placeholders are allowed: a theme kept
    ///   in a synced folder (Documents, the Desktop) is edited in place, and saved through AtomicFile;
    /// - a folder the user can't make files in, or a layout.yaml they can't change (read-only, or denied).
    /// Writing is checked by trying it, with a temporary file that is deleted at once, not by reading permissions:
    /// what Windows does is the answer, whatever the reason.
    public ThemeAccess Check(string themeFolder)
    {
        var folder = ThemeIdentity.FullPath(themeFolder);
        if (IsUnder(folder, ThemeIdentity.FullPath(_installedThemes)))
            return ThemeAccess.ReadOnly("This is an example installed with Marquee, which updates replace. Make a copy to edit it.");

        if (_tags.FirstLink(Path.Combine(folder, ThemeLoader.LayoutFileName)) is { } link)
        {
            return ThemeAccess.ReadOnly(
                $"{link} is a junction, a symbolic link or a mount point, which the editor won't save through. Make a copy to edit it.");
        }

        try
        {
            var probe = Path.Combine(folder, $".marquee-editor-check-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ThemeAccess.ReadOnly($"Windows doesn't let you make files in {folder}, so the editor can't save the theme there. Make a copy to edit it.");
        }

        try
        {
            // Opened for writing and closed again: nothing is written, and the file's time doesn't change.
            using (new FileStream(Path.Combine(folder, ThemeLoader.LayoutFileName), FileMode.Open, FileAccess.Write,
                       FileShare.ReadWrite | FileShare.Delete))
            {
            }
        }
        catch (UnauthorizedAccessException)
        {
            return ThemeAccess.ReadOnly(
                $"Windows doesn't let you change {ThemeLoader.LayoutFileName} (it may be marked read-only), so the editor can't save it. Make a copy to edit it.");
        }
        catch (IOException)
        {
            // Another program has it open just now, which says nothing about whether it can be saved; autosave retries.
        }

        return ThemeAccess.Editable;
    }

    /// The folder a copy of `themeFolder` would be made in: the first free one of `<name>-copy`, `<name>-copy-2` and
    /// so on in the user's themes. A copy of a copy counts on from its original's name.
    public string CopyFolderFor(string themeFolder)
    {
        var name = CopySuffix().Replace(Path.GetFileName(ThemeIdentity.FullPath(themeFolder)), "");
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(_userThemes, n == 1 ? $"{name}-copy" : string.Create(CultureInfo.InvariantCulture, $"{name}-copy-{n}"));
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
    }

    /// Copies the theme in `themeFolder`, every file, into CopyFolderFor's folder, and returns that folder. The files
    /// lose any read-only mark the install gave them; links in the theme aren't followed. The copy is made beside it
    /// under a temporary name and renamed in place whole, so a failed copy leaves no half theme behind.
    public string MakeCopy(string themeFolder)
    {
        var source = ThemeIdentity.FullPath(themeFolder);
        return CopyAs(source, CopyFolderFor(source));
    }

    /// Copies the theme in `source`, a full path, into the folder `target` in the user's themes, as MakeCopy does.
    string CopyAs(string source, string target)
    {
        Directory.CreateDirectory(_userThemes);
        var staging = Path.Combine(_userThemes, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            CopyTree(source, staging);
            Directory.Move(staging, target);
            return target;
        }
        catch
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The caller reports the failure; a hidden temporary folder is harmless.
            }

            throw;
        }
    }

    /// Takes `themeFolder` for this editor; null when another editor has it open (ThemeLock.TryTake).
    public static ThemeLock? TryLock(string themeFolder) => ThemeLock.TryTake(themeFolder);

    static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos("*", options))
        {
            var to = Path.Combine(target, Path.GetRelativePath(source, entry.FullName));
            if (entry is DirectoryInfo)
            {
                Directory.CreateDirectory(to);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(entry.FullName, to);
            var attributes = File.GetAttributes(to);
            if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(to, attributes & ~FileAttributes.ReadOnly);
        }
    }

    static bool IsUnder(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"-copy(-[0-9]+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CopySuffix();
}
