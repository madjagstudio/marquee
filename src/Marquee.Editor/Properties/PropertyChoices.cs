using System.Windows.Media;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Schema;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;

namespace Marquee.Editor.Properties;

/// The lists the pickers offer:
/// - fonts: the theme's font files, then Inter (built in), then the fonts installed on this PC;
/// - keys: those the theme reads, then those the samples know;
/// - files: the theme's files of the key's type.
/// The theme's files are read once and kept until Forget (a theme opened, a file imported); the PC's fonts once per
/// run.
public static class PropertyChoices
{
    /// The image types the editor lists for an image key, as the theme loader decodes them through Skia.
    public static IReadOnlyList<string> ImageExtensions { get; } = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".ico"];

    static readonly Lazy<IReadOnlyList<string>> SystemFonts = new(() =>
        System.Windows.Media.Fonts.SystemFontFamilies
            .Select(family => family.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList());

    static readonly object Gate = new();
    static (string Folder, IReadOnlyList<string> Files)? _files;

    /// The font picker's list. A theme file is named as layout.yaml names it (`fonts/Orbitron.ttf`).
    public static IReadOnlyList<string> Fonts(string? themeFolder) =>
    [
        .. ThemeFiles(themeFolder).Where(file => AssetChecks.FontExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())),
        BundledFonts.Family,
        .. SystemFonts.Value.Where(font => !string.Equals(font, BundledFonts.Family, StringComparison.OrdinalIgnoreCase)),
    ];

    /// The key picker's list: the theme's keys first (`now` aside, which is the clock), then the samples' others.
    public static IReadOnlyList<string> Keys(IReadOnlyList<string> themeKeys) =>
        themeKeys.Where(key => key != TestValues.Now).Concat(Samples.Keys).Distinct(StringComparer.Ordinal).ToList();

    /// The theme's files a file key can name: those with one of its extensions, or of the kind it imports, or else any.
    public static IReadOnlyList<string> Files(string? themeFolder, SchemaKey key, AssetKind? kind)
    {
        IReadOnlyList<string> extensions = key.FileTypes.Count > 0 ? key.FileTypes
            : kind switch
            {
                AssetKind.Image => ImageExtensions,
                AssetKind.Font => AssetChecks.FontExtensions,
                AssetKind.Video => AssetChecks.VideoExtensions,
                _ => [],
            };
        return ThemeFiles(themeFolder)
            .Where(file => extensions.Count == 0 || extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .ToList();
    }

    /// Reads the theme's files again next time: one was imported, or another theme opened.
    public static void Forget()
    {
        lock (Gate) _files = null;
    }

    /// Every file in the theme folder but layout.yaml and hidden ones, as layout.yaml names them, in path order.
    static IReadOnlyList<string> ThemeFiles(string? themeFolder)
    {
        if (themeFolder is null) return [];
        lock (Gate)
        {
            if (_files is { } cached && cached.Folder == themeFolder) return cached.Files;
        }

        var files = new List<string>();
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            };
            foreach (var file in Directory.EnumerateFiles(themeFolder, "*", options))
            {
                var relative = Path.GetRelativePath(themeFolder, file).Replace('\\', '/');
                if (relative.Equals(ThemeLoader.LayoutFileName, StringComparison.OrdinalIgnoreCase)
                    || relative.Split('/').Any(step => step.StartsWith('.')))
                    continue;
                files.Add(relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that went or can't be read offers no files; typing a path still works.
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        lock (Gate) _files = (themeFolder, files);
        return files;
    }
}
