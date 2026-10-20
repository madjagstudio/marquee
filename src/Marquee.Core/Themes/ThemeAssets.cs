using System.Diagnostics;
using System.Globalization;
using Marquee.Core.Themes.Validation;
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
    readonly Dictionary<(string Family, int Weight, bool Italic), SKTypeface> _families = [];
    readonly Dictionary<ShaderLayer, SKRuntimeEffect> _shaders = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<VideoLayer, VideoPlayer> _videos = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<VideoWidget, VideoPlayer> _videoWidgets = new(ReferenceEqualityComparer.Instance);

    // Set from the thread that disposes the cache, read by the render thread.
    volatile bool _detached;

    SKRect _canvas; // what video layers are fitted to

    ThemeAssets(ThemeAssetCache? cache) => Cache = cache;

    /// Its cache was disposed, and with it the images, font files, shaders and players it gave: from now on the theme
    /// draws without them, as if each had been skipped (font files' text in Inter), rather than drawing native objects
    /// already freed, which would end the process. The dispose order is still renderer, theme, then cache; this is
    /// for a render that comes late, and it doesn't make a render running alongside the cache's Dispose safe.
    internal void Detach() => _detached = true;

    /// Where its images, font files, shaders and players come from and go back to, for a theme the editor loaded from
    /// text; null when they are its own, as `marquee run`'s are.
    internal ThemeAssetCache? Cache { get; }

    /// `fps` is the rate the theme will run at, which sets the frame budget each shader layer must fit in. Video
    /// layers and video widgets start playing here, each on its own thread, until the assets are disposed. `refused`
    /// says which files, as layout.yaml names them, are not to be opened at all: `marquee validate` has already reported
    /// them (a file through a link, or over the size limit), and they are left out with no warning of their own. With a
    /// cache, what it already holds for an unchanged file is used, and what is loaded is left in it.
    internal static ThemeAssets Load(string folder, ThemeSpec spec, List<ThemeWarning> warnings, int fps, ShaderProbe probe,
        VideoProbe videoProbe, Func<string, bool>? refused = null, ThemeAssetCache? cache = null)
    {
        var assets = new ThemeAssets(cache) { _canvas = SKRect.Create(spec.Canvas.Size.Width, spec.Canvas.Size.Height) };
        var allowed = (string path) => refused?.Invoke(path) != true;
        try
        {
            var widgets = Flatten(spec.Widgets).ToList();

            var imagePaths = spec.Background.OfType<ImageLayer>().Select(l => l.Path)
                .Concat(widgets.OfType<ImageWidget>().Select(w => w.Path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(allowed);
            foreach (var path in imagePaths)
                if (assets.LoadImage(folder, path, warnings) is { } image)
                    assets._images[path] = image;

            foreach (var layer in spec.Background.OfType<ShaderLayer>().Where(l => allowed(l.Path)))
                if (ShaderAssets.Load(folder, layer, warnings, spec.Canvas.Size, fps, probe, assets) is { } effect)
                    assets._shaders[layer] = effect;

            foreach (var layer in spec.Background.OfType<VideoLayer>().Where(l => allowed(l.Path)))
                if (VideoAssets.Load(folder, layer, warnings, spec.Canvas.Size, videoProbe, assets: assets) is { } player)
                    assets._videos[layer] = player;

            // Each frame is scaled to the widget's box, in the coordinates it is drawn in: its group's, for a child.
            foreach (var widget in widgets.OfType<VideoWidget>().Where(w => allowed(w.Path)))
                if (VideoAssets.Load(folder, widget.Path, widget.Fit, widget.Bounds, warnings, videoProbe, assets: assets) is { } player)
                    assets._videoWidgets[widget] = player;

            var fonts = widgets.SelectMany(w => w.When.Select(r => r.Style).Prepend(w.Style))
                .Concat(widgets.OfType<SpansWidget>().SelectMany(w => w.Spans).Select(span => span.Style))
                .Concat(widgets.OfType<ValueWidget>().SelectMany(w => w.Spans ?? []).Select(span => span.Style))
                .Select(s => s.Font)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var font in fonts)
            {
                if (IsFontFile(font))
                {
                    if (allowed(font) && assets.LoadFontFile(folder, font, warnings) is { } typeface)
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

    public SKImage? Image(string path) => _detached ? null : _images.GetValueOrDefault(path);

    /// The compiled shader for a layer, or null if it was skipped (see the theme's warnings).
    public SKRuntimeEffect? Shader(ShaderLayer layer) => _detached ? null : _shaders.GetValueOrDefault(layer);

    /// The playing clip for a layer, fitted to the canvas, or null if it was skipped (see the theme's warnings). Render
    /// thread only: a clip from a cache may be shared with another theme, whose canvas it was last fitted to.
    public VideoPlayer? Video(VideoLayer layer) => Fitted(_videos.GetValueOrDefault(layer), layer.Fit, _canvas);

    /// The playing clip for a video widget, fitted to its box, or null if it was skipped (see the theme's warnings) or
    /// is hidden. Render thread only: a clip from a cache may be shared with another theme, the editor's last one, whose
    /// box it was last fitted to.
    public VideoPlayer? Video(VideoWidget widget) => Video(widget, widget.Fit, widget.Bounds);

    /// The playing clip for a video widget, fitted to `area` rather than the widget's own box: the theme editor's drag
    /// preview draws a copy of the widget, moved or resized in memory, with the clip the widget was loaded with.
    internal VideoPlayer? Video(VideoWidget widget, ImageFit fit, SKRect area) =>
        Fitted(_videoWidgets.GetValueOrDefault(widget), fit, area);

    VideoPlayer? Fitted(VideoPlayer? player, ImageFit fit, SKRect area)
    {
        if (_detached || player is null) return null;
        player.FitTo(fit, area);
        return player;
    }

    /// Why clips stopped partway since this was last asked, one line per clip, each reported once. The render loop asks
    /// before each frame it renders, and logs them.
    internal IEnumerable<string> TakeVideoErrors()
    {
        var players = _videos.Select(video => (video.Key.Path, Player: video.Value))
            .Concat(_videoWidgets.Select(video => (video.Key.Path, Player: video.Value)));
        foreach (var (path, player) in players)
            if (player.TakeError() is { } error)
                yield return $"Video '{path}' stopped playing and shows its last frame: {error}";
    }

    /// The typeface for a style's font (a family name, or a .ttf/.otf file in the theme folder) at a weight, and with
    /// `italic` an installed family's italic face, when it has one. Inter and a font file are only the faces they are,
    /// so they come back as they are; TextPainter slants a face that isn't italic.
    public SKTypeface Typeface(string? font, int weight, bool italic)
    {
        if (font is null || IsBundled(font)) return BundledFonts.ForWeight(weight);
        if (IsFontFile(font)) return (_detached ? null : _fontFiles.GetValueOrDefault(font)) ?? BundledFonts.ForWeight(weight);

        if (!_families.TryGetValue((font, weight, italic), out var typeface))
        {
            try
            {
                var slant = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
                typeface = SKFontManager.Default.MatchFamily(font, new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, slant))
                    ?? BundledFonts.ForWeight(weight);
            }
            catch (Exception)
            {
                // Called every frame: a font manager failure must not fail every one of them.
                typeface = BundledFonts.ForWeight(weight);
            }

            _families[(font, weight, italic)] = typeface;
        }

        return typeface;
    }

    public void Dispose()
    {
        if (Cache is null)
        {
            StopVideos([.. _videos.Values, .. _videoWidgets.Values]); // stops their threads
            foreach (var image in _images.Values) image.Dispose();
            foreach (var typeface in _fontFiles.Values) typeface.Dispose();
            foreach (var effect in _shaders.Values) effect.Dispose();
        }
        else
        {
            // Other loads may share them: the cache disposes each once no load holds it.
            Cache.Release(this);
        }

        _videos.Clear();
        _videoWidgets.Clear();
        _images.Clear();
        _fontFiles.Clear();
        _shaders.Clear();
        _families.Clear(); // installed-font typefaces belong to the font manager and may be shared with other themes
    }

    /// Stops the players and disposes them. All are signalled first, then each is joined against one deadline from
    /// that moment, so 4 decoders stuck in a call (a network share that went away) hold up a theme switch for one
    /// player's StopTimeout, not 4 of them.
    internal static void StopVideos(IReadOnlyList<VideoPlayer> players)
    {
        foreach (var player in players) player.RequestStop();
        var clock = Stopwatch.StartNew();
        foreach (var player in players) player.Dispose(player.StopTimeout - clock.Elapsed);
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

    SKImage? LoadImage(string folder, string path, List<ThemeWarning> warnings)
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

        return Cache is null ? DecodeImage(path, full, warnings) : Cache.Image(this, full, () => DecodeImage(path, full, warnings));
    }

    static SKImage? DecodeImage(string path, string full, List<ThemeWarning> warnings)
    {
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

    SKTypeface? LoadFontFile(string folder, string font, List<ThemeWarning> warnings)
    {
        if (!TryInsideFolder(folder, font, out var full))
        {
            warnings.Add(new($"Font file '{font}' is not a valid path; using {BundledFonts.Family} instead.", font, Skipped: true));
            return null;
        }

        SKTypeface? typeface = null;
        try
        {
            if (full is not null && File.Exists(full))
            {
                if (Cache is not null && new FileInfo(full).Length > MaxFontBytes)
                {
                    warnings.Add(new(string.Create(CultureInfo.InvariantCulture,
                        $"Font file '{font}' is over the {MaxFontBytes / (1024 * 1024)} MB limit for a file in a theme; using {BundledFonts.Family} instead."),
                        font, Skipped: true));
                    return null;
                }

                typeface = Cache is null ? SKTypeface.FromFile(full) : Cache.Font(this, full, () => FontFromMemory(full));
            }
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            // Reported below like any other unreadable font file. A disposed cache fails the load, as it does for every
            // other file.
        }

        if (typeface is null)
            warnings.Add(new($"Font file '{font}' was not found or could not be read; using {BundledFonts.Family} instead.", font, Skipped: true));
        return typeface;
    }

    /// The largest font file read into memory (FontFromMemory): the limit `marquee validate` sets for any file in a theme.
    static readonly long MaxFontBytes = ThemeLimits.Default.MaxFileBytes;

    /// A font file's typeface from a copy in memory. SKTypeface.FromFile keeps the file open for as long as the typeface
    /// lives, and a cached one lives as long as the editor uses it, which would stop the user replacing the file. The
    /// file is read straight into Skia's memory, the one copy the typeface keeps.
    static SKTypeface? FontFromMemory(string full)
    {
        using var file = File.OpenRead(full);
        using var data = SKData.Create(file, file.Length);
        return data is null ? null : SKTypeface.FromData(data);
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
