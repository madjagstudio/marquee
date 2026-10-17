using SkiaSharp;

namespace Marquee.Core.Themes;

/// Images and fonts a theme refers to, loaded once when the theme loads so the render loop never touches the disk.
/// Anything missing becomes a warning and a fallback (no image; the bundled Inter font), never an error.
public sealed class ThemeAssets : IDisposable
{
    /// Widest or tallest image a theme may use. Themes are shared, and a small file can decode to gigabytes of pixels.
    const int MaxImageSize = 8192;

    readonly Dictionary<string, SKImage> _images = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SKTypeface> _fontFiles = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Family, int Weight), SKTypeface> _families = [];

    ThemeAssets() { }

    internal static ThemeAssets Load(string folder, ThemeSpec spec, List<string> warnings)
    {
        var assets = new ThemeAssets();
        try
        {
            var widgets = Flatten(spec.Widgets).ToList();

            var imagePaths = spec.Background.OfType<ImageLayer>().Select(l => l.Path)
                .Concat(widgets.OfType<ImageWidget>().Select(w => w.Path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var path in imagePaths)
                if (LoadImage(folder, path, warnings) is { } image)
                    assets._images[path] = image;

            var fonts = widgets.SelectMany(w => w.When.Select(r => r.Style).Prepend(w.Style))
                .Select(s => s.Font)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var font in fonts)
            {
                if (IsFontFile(font))
                {
                    if (LoadFontFile(folder, font, warnings) is { } typeface)
                        assets._fontFiles[font] = typeface;
                }
                else if (!IsBundled(font) && !IsInstalled(font))
                {
                    warnings.Add($"Font '{font}' is not installed on this PC; using {BundledFonts.Family} instead.");
                }
            }
        }
        catch
        {
            assets.Dispose();
            throw;
        }

        return assets;
    }

    public SKImage? Image(string path) => _images.GetValueOrDefault(path);

    /// The typeface for a style's font (a family name, or a .ttf/.otf file in the theme folder) at a weight.
    public SKTypeface Typeface(string? font, int weight)
    {
        if (font is null || IsBundled(font)) return BundledFonts.ForWeight(weight);
        if (IsFontFile(font)) return _fontFiles.GetValueOrDefault(font) ?? BundledFonts.ForWeight(weight);

        if (!_families.TryGetValue((font, weight), out var typeface))
        {
            typeface = SKFontManager.Default.MatchFamily(font, new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright))
                ?? BundledFonts.ForWeight(weight);
            _families[(font, weight)] = typeface;
        }

        return typeface;
    }

    public void Dispose()
    {
        foreach (var image in _images.Values) image.Dispose();
        foreach (var typeface in _fontFiles.Values) typeface.Dispose();
        _images.Clear();
        _fontFiles.Clear();
        _families.Clear(); // installed-font typefaces belong to the font manager and may be shared with other themes
    }

    static IEnumerable<WidgetSpec> Flatten(IEnumerable<WidgetSpec> widgets) =>
        widgets.SelectMany(w => w is GroupWidget g ? Flatten(g.Children).Prepend(w) : [w]);

    static bool IsBundled(string font) => string.Equals(font, BundledFonts.Family, StringComparison.OrdinalIgnoreCase);

    static bool IsFontFile(string font) =>
        Path.GetExtension(font).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc";

    static bool IsInstalled(string font)
    {
        try
        {
            // The font manager owns this typeface, and other themes may share it, so it is never disposed here.
            return SKFontManager.Default.MatchFamily(font) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static SKImage? LoadImage(string folder, string path, List<string> warnings)
    {
        if (!TryInsideFolder(folder, path, out var full))
        {
            warnings.Add($"Image '{path}' is not a valid path and was skipped.");
            return null;
        }

        if (full is null)
        {
            warnings.Add($"Image '{path}' is outside the theme folder and was skipped.");
            return null;
        }

        if (!File.Exists(full))
        {
            warnings.Add($"Image '{path}' was not found and was skipped.");
            return null;
        }

        try
        {
            // Read the size from the header before decoding anything.
            using var codec = SKCodec.Create(full);
            if (codec is not null && (codec.Info.Width > MaxImageSize || codec.Info.Height > MaxImageSize))
            {
                warnings.Add($"Image '{path}' is larger than {MaxImageSize}×{MaxImageSize} and was skipped.");
                return null;
            }

            using var bitmap = codec is not null ? SKBitmap.Decode(codec) : null;
            if (bitmap is not null)
                return SKImage.FromBitmap(bitmap);
        }
        catch (Exception)
        {
            // Reported below like any other unreadable image.
        }

        warnings.Add($"Image '{path}' could not be read and was skipped.");
        return null;
    }

    static SKTypeface? LoadFontFile(string folder, string font, List<string> warnings)
    {
        if (!TryInsideFolder(folder, font, out var full))
        {
            warnings.Add($"Font file '{font}' is not a valid path; using {BundledFonts.Family} instead.");
            return null;
        }

        SKTypeface? typeface = null;
        try
        {
            typeface = full is not null && File.Exists(full) ? SKTypeface.FromFile(full) : null;
        }
        catch (Exception)
        {
            // Reported below like any other unreadable font file.
        }

        if (typeface is null)
            warnings.Add($"Font file '{font}' was not found or could not be read; using {BundledFonts.Family} instead.");
        return typeface;
    }

    /// False if `relative` can't be resolved as a path at all (a NUL character, say). Otherwise `full` is the
    /// full path, or null when it leaves the theme folder.
    static bool TryInsideFolder(string folder, string relative, out string? full)
    {
        try
        {
            full = InsideFolder(folder, relative);
            return true;
        }
        catch (Exception)
        {
            full = null;
            return false;
        }
    }

    /// The full path, if `relative` stays inside the theme folder. Shared themes must not reach elsewhere on disk.
    static string? InsideFolder(string folder, string relative)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
