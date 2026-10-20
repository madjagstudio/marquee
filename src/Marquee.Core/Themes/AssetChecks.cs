using System.Globalization;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// The checks a file gets before the theme editor imports it into a theme: the ones the theme loader makes when it
/// loads that kind of file, through the same code, so a file that passes loads, and one that fails says why in a
/// sentence. Images decode with ThemeAssets' decoder within its pixel limit; font files load in Skia as the editor's
/// cache loads them; clips open with Marquee's decoder and play a first frame, and are timed with the decode probe.
///
/// A clip that decodes slower than it plays passes with a warning, not a refusal: the probe times this PC, which may
/// be busy now, and the theme may be shared to a faster one. The loader skips such a clip here, which the warning says.
public sealed class AssetChecks
{
    /// With the real video decode probe, so a clip's warning depends on this PC, as its playing does.
    public static AssetChecks Default { get; } = new(VideoCost.Measure);

    /// The largest file a theme may hold, as `marquee validate` checks it (ThemeLimits.MaxFileBytes, a clip's limit):
    /// the editor imports nothing larger.
    public const long MaxFileBytes = VideoAssets.MaxClipBytes;

    /// The extensions the theme loader reads a style's `font` as a file by; any other font is a family name.
    public static IReadOnlyList<string> FontExtensions { get; } = [".ttf", ".otf", ".ttc"];

    /// The extensions the parser takes for a video layer's or video widget's clip.
    public static IReadOnlyList<string> VideoExtensions => BackgroundParser.VideoExtensions;

    const double BytesPerMB = 1024 * 1024;

    readonly VideoProbe _videoProbe;

    internal AssetChecks(VideoProbe videoProbe) => _videoProbe = videoProbe;

    /// Whether a theme can show the image at `path`: any format Skia decodes, at most 8192 pixels each way.
    public AssetCheck Image(string path)
    {
        if (Missing(path) is { } missing) return missing;

        using var image = ThemeAssets.Decode(path, out var tooLarge);
        if (image is not null) return AssetCheck.Accepted;
        return AssetCheck.Refused(tooLarge
            ? $"It is larger than {ThemeAssets.MaxImageSize}×{ThemeAssets.MaxImageSize} pixels, the largest image a theme can use."
            : "It could not be read as an image. A theme can use PNG, JPEG, WebP or another format Skia reads.");
    }

    /// Whether a theme can draw text in the font file at `path`: a .ttf, .otf or .ttc that Skia loads.
    public AssetCheck Font(string path)
    {
        if (Missing(path) is { } missing) return missing;
        if (!FontExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return AssetCheck.Refused($"A font file must be a {Either(FontExtensions)} file.");

        var length = new FileInfo(path).Length;
        if (length > ThemeAssets.MaxFontBytes)
            return AssetCheck.Refused($"The file is {Megabytes(length)} MB, over the {Megabytes(ThemeAssets.MaxFontBytes)} MB limit for a file in a theme.");

        try
        {
            using var typeface = ThemeAssets.FontFromMemory(path);
            if (typeface is not null) return AssetCheck.Accepted;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Read as any other file that isn't a font.
        }

        return AssetCheck.Refused("It could not be read as a font.");
    }

    /// Whether a theme can play the clip at `path`: a file the parser takes, within the clip size limit, that Marquee's
    /// decoder opens and plays a frame of. It is timed at its own size, as the box it will play in isn't chosen yet: a
    /// box smaller than the clip costs a little less, a larger one a little more. Too slow is a warning (see the class).
    public AssetCheck Video(string path)
    {
        if (Missing(path) is { } missing) return missing;
        if (!VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return AssetCheck.Refused($"A clip must be a {Either(VideoExtensions)} file.");

        var length = new FileInfo(path).Length;
        if (length > VideoAssets.MaxClipBytes)
            return AssetCheck.Refused($"The file is {Megabytes(length)} MB, over the {Megabytes(VideoAssets.MaxClipBytes)} MB limit for a clip.");

        IVideoDecoder? decoder = null;
        try
        {
            decoder = VideoDecoders.Open(path);
            var scaler = new FrameScaler(ImageFit.Contain, decoder.Size, SKRect.Create(decoder.Size.Width, decoder.Size.Height));
            var slow = VideoAssets.TooSlow(decoder, scaler, _videoProbe);
            decoder.Rewind();

            // As the loader starts it, which refuses a clip without a frame. The player owns the decoder from here.
            var player = new VideoPlayer(decoder, scaler);
            decoder = null;
            player.Dispose();
            return slow is null ? AssetCheck.Accepted : AssetCheck.Warned($"On this PC the theme would skip it. {slow}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The loader's words: a VideoDecodeException says what's wrong with the clip.
            return AssetCheck.Refused(ex is VideoDecodeException ? ex.Message : $"It could not be played: {ex.Message}");
        }
        finally
        {
            decoder?.Dispose();
        }
    }

    static AssetCheck? Missing(string path) => File.Exists(path) ? null : AssetCheck.Refused("The file was not found.");

    /// ".a, .b or .c".
    static string Either(IReadOnlyList<string> extensions) =>
        extensions.Count == 1 ? extensions[0] : $"{string.Join(", ", extensions.SkipLast(1))} or {extensions[^1]}";

    static string Megabytes(long bytes) => Math.Ceiling(bytes / BytesPerMB).ToString("N0", CultureInfo.InvariantCulture);
}

/// What a file's check found: Refusal says why it can't be used, and Warning what a file that can be used will do.
/// Each is a sentence, or null.
public sealed record AssetCheck(string? Refusal, string? Warning)
{
    public static AssetCheck Accepted { get; } = new(null, null);

    public bool IsAccepted => Refusal is null;

    public static AssetCheck Refused(string reason) => new(reason, null);

    public static AssetCheck Warned(string warning) => new(null, warning);
}
