using System.Globalization;
using Marquee.Core.Files;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// "Import…": copies a file the user chose into the theme folder, so the theme carries it wherever it is shared.
/// - The file is checked first, as the theme loader would load it (AssetChecks), and a refusal says why. A clip too
///   slow for this PC is taken, with a warning.
/// - It goes into images\, fonts\, video\ or shaders\ by its kind, under its own name, or with a number added when that is taken
///   (logo-2.png). A file already in the theme folder is used where it is.
/// - The copy is written under a temporary name and renamed (AtomicFile.TryCopyNew). A link on the way is refused, as
///   for a save (ReparseTags): OneDrive's placeholders are written through, and a theme folder that is gone isn't
///   made again.
/// - Files over the theme's per-file limit, 200 MB, are refused before they are read, and a copy stops at the limit, so
///   a file that grows or is swapped after the check isn't taken either.
///
/// AddImage, AddVideo, AddLayer and SetFile import, then make the edit that uses the file, as one undo step. Undo takes the
/// reference away and leaves the file, which "Clean up unused files" (UnusedFiles) deletes later; a copy the edit
/// doesn't take is deleted at once.
public sealed class AssetImport
{
    /// The largest file imported: the theme's per-file limit (AssetChecks.MaxFileBytes), so nothing imported makes the
    /// theme fail `marquee validate`.
    public const long MaxBytes = AssetChecks.MaxFileBytes;

    /// The highest number tried for a name in use, so a folder of a million logos ends the search.
    const int MaxNumber = 9999;

    const double BytesPerMB = 1024 * 1024;

    readonly AssetChecks _checks;
    readonly long _maxBytes;
    readonly ReparseTags _tags;

    /// Imports into the theme folder `themeFolder`, checking files as `marquee run` loads them on this PC.
    public AssetImport(string themeFolder)
        : this(themeFolder, AssetChecks.Default)
    {
    }

    /// `checks`, `maxBytes` and `tags` are AssetChecks.Default, MaxBytes and ReparseTags.Windows except in tests.
    internal AssetImport(string themeFolder, AssetChecks checks, long maxBytes = MaxBytes, ReparseTags? tags = null)
    {
        ThemeFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(themeFolder));
        _checks = checks;
        _maxBytes = maxBytes;
        _tags = tags ?? ReparseTags.Windows;
    }

    /// The theme folder's full path.
    public string ThemeFolder { get; }

    /// The folder a kind of file goes into, relative to the theme folder.
    public static string FolderFor(AssetKind kind) => kind switch
    {
        AssetKind.Image => "images",
        AssetKind.Font => "fonts",
        AssetKind.Video => "video",
        AssetKind.Shader => "shaders",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// The kind of file the key at `key` names, or null when it names none the editor imports: a style's `font`, a
    /// background layer's `image`, `video` or `shader`, and an image or video widget's `src`.
    public static AssetKind? KindFor(LayoutDocument document, YamlPath key)
    {
        if (key.Last is not { IsKey: true } last) return null;
        return last.Key switch
        {
            "font" => AssetKind.Font,
            "image" => AssetKind.Image,
            "video" => AssetKind.Video,
            "shader" => AssetKind.Shader,
            "src" when document.TryGetScalar(key.Parent!.Child("type"), out var type) => type switch
            {
                "image" => AssetKind.Image,
                "video" => AssetKind.Video,
                _ => null,
            },
            _ => null,
        };
    }

    /// Checks the file at `source` as a `kind` and copies it into the theme folder (see the class), returning the path
    /// layout.yaml is to name it by. Throws AssetImportException, with the reason, for a file refused, a link on the
    /// way, or a copy that failed; nothing is left behind then.
    public ImportedFile Import(string source, AssetKind kind)
    {
        string full;
        try
        {
            full = Path.GetFullPath(source);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AssetImportException("That is not a valid path.", ex);
        }

        var file = new FileInfo(full);
        if (!file.Exists) throw new AssetImportException("The file was not found.");
        if (file.Length > _maxBytes)
            throw new AssetImportException($"The file is {Megabytes(file.Length)} MB, over the {Megabytes(_maxBytes)} MB limit for importing.");

        var check = kind switch
        {
            AssetKind.Image => _checks.Image(full),
            AssetKind.Font => _checks.Font(full),
            AssetKind.Video => _checks.Video(full),
            AssetKind.Shader => _checks.Shader(full),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (check.Refusal is { } refusal) throw new AssetImportException(refusal);

        // Already part of the theme: a second copy would only be a file to clean up later.
        if (ThemePaths.Relative(ThemeFolder, full) is { } relative && _tags.FirstLink(full) is null)
            return new ImportedFile(relative, kind, check.Warning, Copied: false);

        var folder = FolderFor(kind);
        var name = Copy(full, folder);
        return new ImportedFile($"{folder}/{name}", kind, check.Warning, Copied: true);
    }

    /// Imports the image at `source` and adds an image widget showing it (AddWidget's, in the middle of the canvas or
    /// of the group at `group`), as one undo step. Throws as Import and EditorSession.Apply do.
    public ImportedFile AddImage(EditorSession session, string source, YamlPath? group = null) =>
        Apply(session, source, AssetKind.Image, file => new AddImportedWidget("image", file.Path, group));

    /// Imports the clip at `source` and adds a video widget playing it, as AddImage adds an image.
    public ImportedFile AddVideo(EditorSession session, string source, YamlPath? group = null) =>
        Apply(session, source, AssetKind.Video, file => new AddImportedWidget("video", file.Path, group));

    public ImportedFile AddModule(EditorSession session, Module module, string source, AssetKind kind, SkiaSharp.SKPoint? at, YamlPath? group) =>
        Apply(session, source, kind, file => new AddModule(module with
        {
            Yaml = LayoutDocument.Open(module.Yaml).SetScalar(YamlPath.Root.Child("src"), file.Path).Text,
            NeedsFile = false,
        }, at, group));

    /// Imports a background's image, video or shader and adds its layer above the selection as one undo step.
    /// A refused edit discards a new copy. Fill has no file; add it directly with the AddLayer command.
    public ImportedFile AddLayer(EditorSession session, LayerKindChoice kind, string source)
    {
        var assetKind = kind switch
        {
            LayerKindChoice.Image => AssetKind.Image,
            LayerKindChoice.Video => AssetKind.Video,
            LayerKindChoice.Shader => AssetKind.Shader,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "Only image, video and shader layers import files."),
        };
        return Apply(session, source, assetKind, file => new AddLayer(kind, file.Path, Model.AddLayer.AddIndex(session)));
    }

    /// Imports the file at `source` for the file key at `key` (KindFor says which kind it takes) and sets the key to
    /// it, adding the key when the file hasn't it, as one undo step. Throws ArgumentException, before anything is
    /// copied, for a key that takes no file the editor imports, and otherwise as Import and EditorSession.Apply do.
    public ImportedFile SetFile(EditorSession session, YamlPath key, string source)
    {
        ArgumentNullException.ThrowIfNull(session);
        var kind = KindFor(session.Document, key)
            ?? throw new ArgumentException($"{key} doesn't name an image, font, video or shader file.", nameof(key));
        return Apply(session, source, kind, file => new SetImportedFile(key, file.Path));
    }

    /// Imports a replacement layer's file, discarding the copy if the kind change is refused.
    public ImportedFile ChangeLayerKind(EditorSession session, YamlPath layer, LayerKindChoice kind, string source) =>
        Apply(session, source, kind switch
        {
            LayerKindChoice.Image => AssetKind.Image,
            LayerKindChoice.Video => AssetKind.Video,
            LayerKindChoice.Shader => AssetKind.Shader,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        }, file => new ChangeLayerKind(layer, kind, file.Path));

    ImportedFile Apply(EditorSession session, string source, AssetKind kind, Func<ImportedFile, EditorCommand> edit)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!string.Equals(session.Folder, ThemeFolder, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The session has {session.Folder} open, not {ThemeFolder}.", nameof(session));

        var file = Import(source, kind);
        try
        {
            session.Apply(edit(file));
        }
        catch
        {
            if (file.Copied) Discard(file);
            throw;
        }

        return file;
    }

    /// Copies `full` into `folder` under its own name, or the first numbered one free, and returns the name.
    string Copy(string full, string folder)
    {
        var name = Path.GetFileName(full);
        var stem = Path.GetFileNameWithoutExtension(full);
        var extension = Path.GetExtension(full);
        var into = Path.Combine(ThemeFolder, folder);
        try
        {
            if (_tags.FirstLink(into) is { } link) throw ReparseTags.Refusal(into, link);
            AtomicFile.MakeFolder(into);
            for (var number = 1; number <= MaxNumber; number++)
            {
                var candidate = number == 1 ? name : $"{stem}-{number.ToString(CultureInfo.InvariantCulture)}{extension}";
                if (AtomicFile.TryCopyNew(full, Path.Combine(into, candidate), _maxBytes)) return candidate;
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new AssetImportException(
                "The theme's folder is gone (moved, renamed or deleted), so the file wasn't copied into it, and it wasn't made again.", ex);
        }
        catch (FileTooLargeException ex)
        {
            // It grew, or was swapped for a larger file, after the size check: nothing was left behind.
            throw new AssetImportException($"The file grew past the {Megabytes(_maxBytes)} MB limit for importing as it was copied.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // UnsafePathException already says what the link is and where.
            throw new AssetImportException(
                ex is UnsafePathException ? ex.Message : $"It could not be copied into the theme folder: {ex.Message}", ex);
        }

        throw new AssetImportException(string.Create(CultureInfo.InvariantCulture,
            $"The theme's {folder} folder already has {name} and every numbered name for it up to {MaxNumber}."));
    }

    /// Deletes a copy no edit took, unless a link has come onto its path since. One that won't go is only an unused
    /// file, which the clean-up lists.
    internal void Discard(ImportedFile file)
    {
        var path = Path.GetFullPath(Path.Combine(ThemeFolder, file.Path));
        try
        {
            if (_tags.FirstLink(path) is null) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for "Clean up unused files".
        }
    }

    static string Megabytes(long bytes) => Math.Ceiling(bytes / BytesPerMB).ToString("N0", CultureInfo.InvariantCulture);
}
