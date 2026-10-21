using System.Globalization;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// Opens a video layer's or video widget's clip and starts playing it when the theme loads, so a clip that can't play
/// surfaces once, as a warning that says why, and the render loop never meets it. A layer or widget that can't play is
/// skipped.
static class VideoAssets
{
    /// Largest clip file a theme may use. Themes are shared; a minute of 1080p H.264 is about 40 MB.
    public const long MaxClipBytes = 200L * 1024 * 1024;

    const double BytesPerMB = 1024 * 1024;

    /// A player for the layer's clip, fitted to the canvas, or null when the layer is skipped (see the other Load).
    public static VideoPlayer? Load(string folder, VideoLayer layer, List<ThemeWarning> warnings, SKSizeI canvas, VideoProbe probe,
        long maxBytes = MaxClipBytes, ThemeAssets? assets = null) =>
        Load(folder, layer.Path, layer.Fit, SKRect.Create(canvas.Width, canvas.Height), warnings, probe, maxBytes, assets);

    /// A player for the clip at `path`, its frames fitted to `area` as `fit` says, or null when it is skipped: the clip
    /// is missing, too large, can't be decoded, or `probe` says it decodes slower than it plays. `maxBytes` is
    /// MaxClipBytes except in tests. With `assets` loading through a cache, the player, or why there is none, comes from
    /// there, and the cache owns the player.
    public static VideoPlayer? Load(string folder, string path, ImageFit fit, SKRect area, List<ThemeWarning> warnings,
        VideoProbe probe, long maxBytes = MaxClipBytes, ThemeAssets? assets = null)
    {
        if (!ThemeAssets.TryInsideFolder(folder, path, out var full))
            return Skip(warnings, path, "It is not a valid path.");
        if (full is null)
            return Skip(warnings, path, "It is outside the theme folder.");
        var file = new FileInfo(full);
        if (!file.Exists)
            return Skip(warnings, path, "The file was not found.");
        if (file.Length > maxBytes)
        {
            return Skip(warnings, path, string.Create(CultureInfo.InvariantCulture,
                $"The file is {Math.Ceiling(file.Length / BytesPerMB):N0} MB, over the {maxBytes / BytesPerMB:N0} MB limit."));
        }

        var clip = assets?.Cache is { } cache
            ? cache.Clip(assets, full, fit, area, () => Open(full, fit, area, probe))
            : Open(full, fit, area, probe);
        return clip.Player ?? Skip(warnings, path, clip.Refusal!);
    }

    /// The clip playing, or why it can't play.
    static OpenedClip Open(string full, ImageFit fit, SKRect area, VideoProbe probe)
    {
        IVideoDecoder? decoder = null;
        try
        {
            decoder = VideoDecoders.Open(full);
            var scaler = new FrameScaler(fit, decoder.Size, area);
            if (TooSlow(decoder, scaler, probe) is { } slow)
            {
                decoder.Dispose();
                return new OpenedClip(null, slow);
            }

            decoder.Rewind();
            return new OpenedClip(new VideoPlayer(decoder, scaler), null); // the player owns the decoder from here
        }
        catch (Exception ex)
        {
            // A VideoDecodeException says what's wrong with the clip; anything else (a codec bug, say) is still just a
            // clip that can't play, not a theme that can't load. A file that couldn't be read (another program holds it
            // locked, say) says nothing about the clip, so that refusal is only for now.
            decoder?.Dispose();
            return new OpenedClip(null, ex is VideoDecodeException ? ex.Message : $"It could not be played: {ex.Message}",
                ForNow: ex is IOException or UnauthorizedAccessException);
        }
    }

    /// Why the clip can't keep up with itself, or null. A slow measurement is taken again, and the faster of the two
    /// counts, so a hiccup elsewhere on the PC doesn't cost a theme its background.
    internal static string? TooSlow(IVideoDecoder decoder, FrameScaler scaler, VideoProbe probe)
    {
        var cost = probe(decoder, scaler);
        if (cost.PerFrame > cost.FrameDuration)
        {
            decoder.Rewind();
            var again = probe(decoder, scaler);
            if (again.PerFrame < cost.PerFrame) cost = again;
        }

        if (cost.PerFrame <= cost.FrameDuration) return null;
        var invariant = CultureInfo.InvariantCulture;
        return $"Decoding a frame takes about {cost.PerFrame.TotalMilliseconds.ToString("N0", invariant)} ms, longer than the " +
            $"{cost.FrameDuration.TotalMilliseconds.ToString("0", invariant)} ms each frame is shown, so playback can't keep up. " +
            "Use a smaller clip, or one with fewer frames per second.";
    }

    static VideoPlayer? Skip(List<ThemeWarning> warnings, string path, string reason)
    {
        warnings.Add(new($"Video '{path}' was skipped: {reason}", path, Skipped: true));
        return null;
    }
}

/// A clip opened for playing: its player, or why it has none (a sentence for the warning, without the clip's name).
/// ForNow says the refusal comes from reading the file, not from the clip, and may not hold on the next try.
sealed record OpenedClip(VideoPlayer? Player, string? Refusal, bool ForNow = false);
