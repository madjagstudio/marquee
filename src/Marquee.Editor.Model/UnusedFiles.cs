using Marquee.Core.Files;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// "Clean up unused files": the files in the theme folder that nothing in layout.yaml names, and deleting the ones the
/// user ticks. Imports and undone edits leave such files behind (AssetImport).
///
/// What counts as named: layout.yaml is parsed as YAML (LayoutDocument), and every single value in it, at any depth and
/// keys included, is read as a path relative to the theme folder, as the theme loader resolves one (either slash, any
/// letter case, `./`). That takes in every place a theme names a file without a list of keys to keep up to date:
/// background image, video and shader layers; image and video widgets, in groups too; and font files in named styles
/// (used by a widget or not), inline styles, `extends` styles, `when` rules' styles, spans, value spans, and the styles
/// dial and donut labels are drawn in. It errs on keeping: a text that happens to name a file keeps it. A line
/// commented out names nothing. A read-only file is deleted like any other.
///
/// Never listed or deleted: layout.yaml itself; hidden files and folders, by the Hidden or System attribute or a dot
/// name (.git, an editor's temporary file); and links (junctions, symbolic links, mount points), which are neither
/// listed nor followed. Links by the editor's rule (ReparseTags): a OneDrive folder or file, a cloud placeholder, is
/// none, so a theme kept in OneDrive is cleaned up in place. The delete is the editor's own (ThemeFileDelete), which
/// holds to that rule without following a link; SafeFile's stays strict for the tray.
public static class UnusedFiles
{
    /// The unused files of the session's theme, against the session's text, which may not be saved yet.
    public static IReadOnlyList<UnusedFile> List(EditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return List(session.Folder, session.Document.Text, ReparseTags.Windows);
    }

    /// The files in `themeFolder` that `layoutText` doesn't name, in path order. Throws LayoutEditException for text
    /// that isn't YAML the editor reads: what it names can't be told then.
    public static IReadOnlyList<UnusedFile> List(string themeFolder, string layoutText) =>
        List(themeFolder, layoutText, ReparseTags.Windows);

    /// List, with `tags` telling a link from a cloud placeholder: ReparseTags.Windows except in tests.
    internal static IReadOnlyList<UnusedFile> List(string themeFolder, string layoutText, ReparseTags tags)
    {
        var root = Root(themeFolder);
        var named = Named(root, layoutText);
        return Candidates(root, tags)
            .Where(file => !named.Contains(file.FullName))
            .Select(file => new UnusedFile(ThemePaths.Relative(root, file.FullName)!, file.Length))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// Deletes the session's chosen files, checked against the session's text.
    public static IReadOnlyList<FileDeletion> Delete(EditorSession session, IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Delete(session.Folder, session.Document.Text, files, ReparseTags.Windows);
    }

    /// Deletes each of `files` (paths relative to `themeFolder`, as List gives them) that is still unused: checked
    /// again, one by one, against `layoutText` and against layout.yaml on disk when that differs (a hand edit the
    /// editor hasn't reloaded yet). A file either names is kept, as is one outside the theme folder, hidden, reached
    /// through a link, or layout.yaml itself. Returns one result per file, in order, with the reason for each kept.
    /// Throws LayoutEditException, deleting nothing, when either text isn't YAML the editor reads.
    public static IReadOnlyList<FileDeletion> Delete(string themeFolder, string layoutText, IEnumerable<string> files) =>
        Delete(themeFolder, layoutText, files, ReparseTags.Windows);

    /// Delete, with `tags` telling a link from a cloud placeholder: ReparseTags.Windows except in tests.
    internal static IReadOnlyList<FileDeletion> Delete(string themeFolder, string layoutText, IEnumerable<string> files,
        ReparseTags tags)
    {
        ArgumentNullException.ThrowIfNull(files);
        var root = Root(themeFolder);
        var named = Named(root, layoutText);
        if (OnDisk(root) is { } disk && disk != layoutText) named.UnionWith(Named(root, disk));
        return files.Select(file => Delete(root, file, named, tags)).ToList();
    }

    static FileDeletion Delete(string root, string file, HashSet<string> named, ReparseTags tags)
    {
        var full = ThemePaths.Resolve(root, file);
        if (full is null) return new(file, "It is outside the theme folder.");
        if (IsLayout(root, full)) return new(file, "It is the theme's layout.yaml.");
        if (ThemePaths.Attributes(full) is not { } attributes) return new(file, "It is no longer there.");
        if (ThemePaths.Hidden(root, full)) return new(file, "It is hidden, or in a hidden folder, which clean-up leaves alone.");
        if (tags.FirstLink(full) is not null) return new(file, LinkedReason);
        if (attributes.HasFlag(FileAttributes.Directory)) return new(file, "It is a folder, not a file.");
        if (named.Contains(full)) return new(file, "layout.yaml uses it now.");

        try
        {
            // Refuses a link made since the check above, rather than deleting through it. A read-only file goes too: the
            // user ticked it, and a theme unzipped or copied from a read-only place often has them.
            ThemeFileDelete.Delete(full, tags, evenReadOnly: true);
            return new(file, null);
        }
        catch (UnsafePathException)
        {
            return new(file, LinkedReason);
        }
        catch (UnauthorizedAccessException) when (attributes.HasFlag(FileAttributes.ReadOnly))
        {
            return new(file, "It is read-only, and Windows here can't delete it as it is. Clear Read-only in its Properties, then "
                + "clean up again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(file, $"It could not be deleted: {ex.Message}");
        }
    }

    const string LinkedReason = "It is a link, or in a linked folder, so it may be a file somewhere else.";

    /// The full paths of the files every single value in `layoutText` names inside the theme folder (see the class).
    static HashSet<string> Named(string root, string layoutText)
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in Scalars(LayoutDocument.Open(layoutText).ValueAt(YamlPath.Root)))
            if (ThemePaths.Resolve(root, text) is { } full)
                named.Add(full);
        return named;
    }

    static IEnumerable<string> Scalars(YamlValue? value) => value switch
    {
        YamlText text => [text.Value],
        YamlList list => list.Items.SelectMany(Scalars),
        YamlMap map => map.Entries.SelectMany(entry => Scalars(entry.Value).Prepend(entry.Key)),
        _ => [],
    };

    /// Every file the clean-up may offer: all but layout.yaml, hidden files and folders, and links (not cloud
    /// placeholders: ReparseTags), which it doesn't look through.
    static IEnumerable<FileInfo> Candidates(string root, ReparseTags tags)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
        var pending = new Stack<DirectoryInfo>([new DirectoryInfo(root)]);
        while (pending.TryPop(out var folder))
        {
            foreach (var entry in folder.EnumerateFileSystemInfos("*", options))
            {
                if (entry.Name.StartsWith('.')
                    || (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0
                    || tags.IsLink(entry.FullName, entry.Attributes))
                    continue;
                if (entry is DirectoryInfo inside) pending.Push(inside);
                else if (!IsLayout(root, entry.FullName)) yield return (FileInfo)entry;
            }
        }
    }

    /// layout.yaml as it is on disk, or null when there is none.
    static string? OnDisk(string root)
    {
        var path = Path.Combine(root, ThemeLoader.LayoutFileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    static bool IsLayout(string root, string full) =>
        string.Equals(full, Path.Combine(root, ThemeLoader.LayoutFileName), StringComparison.OrdinalIgnoreCase);

    static string Root(string themeFolder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(themeFolder));
}

/// A file nothing in layout.yaml names: its path relative to the theme folder, with forward slashes, and its size.
public sealed record UnusedFile(string Path, long Bytes);

/// What deleting a file did: Refusal is null when it was deleted, and otherwise says why it was kept.
public sealed record FileDeletion(string File, string? Refusal)
{
    public bool Deleted => Refusal is null;
}
