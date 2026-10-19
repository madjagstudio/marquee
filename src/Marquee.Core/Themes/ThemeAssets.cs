using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// Images, fonts, shaders and video players a theme refers to, loaded once when the theme loads so the render loop
/// never touches the disk (video players read their clips on their own threads). Anything missing becomes a warning
/// and a fallback (no image or layer; the bundled Inter font), never an error.
public sealed class ThemeAssets : IDisposable
{
    /// Widest or tallest image a theme may use. Themes are shared, and a small file can decode to gigabytes of pixels.
    const int MaxImageSize = 8192;

    readonly Dictionary<string, SKImage> _images = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SKTypeface> _fontFiles = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Family, int Weight), SKTypeface> _families = [];
    readonly Dictionary<ShaderLayer, SKRuntimeEffect> _shaders = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<VideoLayer, VideoPlayer> _videos = new(ReferenceEqualityComparer.Instance);

    ThemeAssets() { }

    /// `fps` is the rate the theme will run at, which sets the frame budget each shader layer must fit in. Video
    /// layers start playing here, each on its own thread, until the assets are disposed. `refused` says which files, as
    /// layout.yaml names them, are not to be opened at all: `marquee validate` has already reported them (a file through
    /// a link, or over the size limit), and they are left out with no warning of their own.
    internal static ThemeAssets Load(string folder, ThemeSpec spec, List<ThemeWarning> warnings, int fps, ShaderProbe probe,
        VideoProbe videoProbe, Func<string, bool>? refused = null)
    {
        var assets = new ThemeAssets();
        var allowed = (string path) => refused?.Invoke(path) != true;
        try
        {
            var widgets = Flatten(spec.Widgets).ToList();

            var imagePaths = spec.Background.OfType<ImageLayer>().Select(l => l.Path)
                .Concat(widgets.OfType<ImageWidget>().Select(w => w.Path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(allowed);
            foreach (var path in imagePaths)
                if (LoadImage(folder, path, warnings) is { } image)
                    assets._images[path] = image;

            foreach (var layer in spec.Background.OfType<ShaderLayer>().Where(l => allowed(l.Path)))
                if (ShaderAssets.Load(folder, layer, warnings, spec.Canvas.Size, fps, probe) is { } effect)
                    assets._shaders[layer] = effect;

            foreach (var layer in spec.Background.OfType<VideoLayer>().Where(l => allowed(l.Path)))
                if (VideoAssets.Load(folder, layer, warnings, spec.Canvas.Size, videoProbe) is { } player)
                    assets._videos[layer] = player;

            var fonts = widgets.SelectMany(w => w.When.Select(r => r.Style).Prepend(w.Style))
                .Select(s => s.Font)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var font in fonts)
            {
                if (IsFontFile(font))
                {
                    if (allowed(font) && LoadFontFile(folder, font, warnings) is { } typeface)
                        assets._fontFiles[font] = typeface;
                }
                else if (!IsBundled(font) && !IsInstalled(font))
                {
                    warnings.Add(new($"Font '{font}' is not installed on this PC; using {BundledFonts.Family} instead.", null, Skipped: false));
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

    /// The compiled shader for a layer, or null if it was skipped (see the theme's warnings).
    public SKRuntimeEffect? Shader(ShaderLayer layer) => _shaders.GetValueOrDefault(layer);

    /// The playing clip for a layer, or null if it was skipped (see the theme's warnings).
    public VideoPlayer? Video(VideoLayer layer) => _videos.GetValueOrDefault(layer);

    /// Why clips stopped partway since this was last asked, one line per clip, each reported once. The render loop asks
    /// before each frame it renders, and logs them.
    internal IEnumerable<string> TakeVideoErrors()
    {
        foreach (var (layer, player) in _videos)
            if (player.TakeError() is { } error)
                yield return $"Video '{layer.Path}' stopped playing and shows its last frame: {error}";
    }

    /// The typeface for a style's font (a family name, or a .ttf/.otf file in the theme folder) at a weight.
    public SKTypeface Typeface(string? font, int weight)
    {
        if (font is null || IsBundled(font)) return BundledFonts.ForWeight(weight);
        if (IsFontFile(font)) return _fontFiles.GetValueOrDefault(font) ?? BundledFonts.ForWeight(weight);

        if (!_families.TryGetValue((font, weight), out var typeface))
        {
            try
            {
                typeface = SKFontManager.Default.MatchFamily(font, new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright))
                    ?? BundledFonts.ForWeight(weight);
            }
            catch (Exception)
            {
                // Called every frame: a font manager failure must not fail every one of them.
                typeface = BundledFonts.ForWeight(weight);
            }

            _families[(font, weight)] = typeface;
        }

        return typeface;
    }

    public void Dispose()
    {
        foreach (var player in _videos.Values) player.Dispose(); // stops their threads
        _videos.Clear();
        foreach (var image in _images.Values) image.Dispose();
        foreach (var typeface in _fontFiles.Values) typeface.Dispose();
        foreach (var effect in _shaders.Values) effect.Dispose();
        _images.Clear();
        _fontFiles.Clear();
        _shaders.Clear();
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

    static SKImage? LoadImage(string folder, string path, List<ThemeWarning> warnings)
    {
        if (!TryInsideFolder(folder, path, out var full))
        {
            warnings.Add(new($"Image '{path}' is not a valid path and was skipped.", path, Skipped: true));
            return null;
        }

        if (full is null)
        {
            warnings.Add(new($"Image '{path}' is outside the theme folder and was skipped.", path, Skipped: true));
            return null;
        }

        if (!File.Exists(full))
        {
            warnings.Add(new($"Image '{path}' was not found and was skipped.", path, Skipped: true));
            return null;
        }

        try
        {
            // Read the size from the header before decoding anything.
            using var codec = SKCodec.Create(full);
            if (codec is not null && (codec.Info.Width > MaxImageSize || codec.Info.Height > MaxImageSize))
            {
                warnings.Add(new($"Image '{path}' is larger than {MaxImageSize}×{MaxImageSize} and was skipped.", path, Skipped: true));
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

        warnings.Add(new($"Image '{path}' could not be read and was skipped.", path, Skipped: true));
        return null;
    }

    static SKTypeface? LoadFontFile(string folder, string font, List<ThemeWarning> warnings)
    {
        if (!TryInsideFolder(folder, font, out var full))
        {
            warnings.Add(new($"Font file '{font}' is not a valid path; using {BundledFonts.Family} instead.", font, Skipped: true));
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
            warnings.Add(new($"Font file '{font}' was not found or could not be read; using {BundledFonts.Family} instead.", font, Skipped: true));
        return typeface;
    }

    /// False if `relative` can't be resolved as a path at all (a NUL character, say). Otherwise `full` is the
    /// full path, or null when it leaves the theme folder.
    internal static bool TryInsideFolder(string folder, string relative, out string? full)
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
