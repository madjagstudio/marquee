using SkiaSharp;

namespace Marquee.Core.Themes;

/// Images and fonts a theme refers to, loaded once when the theme loads so the render loop never touches the disk.
/// Anything missing becomes a warning and a fallback (no image; the bundled Inter font), never an error.
public sealed class ThemeAssets : IDisposable
{
    readonly Dictionary<string, SKImage> _images = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SKTypeface> _fontFiles = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Family, int Weight), SKTypeface> _families = [];

    ThemeAssets() { }

    internal static ThemeAssets Load(string folder, ThemeSpec spec, List<string> warnings)
    {
        var assets = new ThemeAssets();
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
            else if (!IsBundled(font))
            {
                // The font manager owns this typeface, and other themes may share it, so it is never disposed here.
                var match = SKFontManager.Default.MatchFamily(font);
                if (match is null)
                    warnings.Add($"Font '{font}' is not installed on this PC; using {BundledFonts.Family} instead.");
            }
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

    static SKImage? LoadImage(string folder, string path, List<string> warnings)
    {
        if (InsideFolder(folder, path) is not { } full)
        {
            warnings.Add($"Image '{path}' is outside the theme folder and was skipped.");
            return null;
        }

        if (!File.Exists(full))
        {
            warnings.Add($"Image '{path}' was not found and was skipped.");
            return null;
        }

        using var bitmap = SKBitmap.Decode(full);
        if (bitmap is null)
        {
            warnings.Add($"Image '{path}' could not be read and was skipped.");
            return null;
        }

        return SKImage.FromBitmap(bitmap);
    }

    static SKTypeface? LoadFontFile(string folder, string font, List<string> warnings)
    {
        var full = InsideFolder(folder, font);
        var typeface = full is not null && File.Exists(full) ? SKTypeface.FromFile(full) : null;
        if (typeface is null)
            warnings.Add($"Font file '{font}' was not found or could not be read; using {BundledFonts.Family} instead.");
        return typeface;
    }

    /// The full path, if `relative` stays inside the theme folder. Shared themes must not reach elsewhere on disk.
    static string? InsideFolder(string folder, string relative)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
