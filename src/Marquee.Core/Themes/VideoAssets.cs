using System.Globalization;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// Opens a video layer's clip and starts playing it when the theme loads, so a clip that can't play surfaces once, as a
/// warning that says why, and the render loop never meets it. A layer that can't play is skipped.
static class VideoAssets
{
    /// Largest clip file a theme may use. Themes are shared; a minute of 1080p H.264 is about 40 MB.
    public const long MaxClipBytes = 200L * 1024 * 1024;

    const double BytesPerMB = 1024 * 1024;

    /// A player for the clip, or null when the layer is skipped: the clip is missing, too large, can't be decoded, or
    /// `probe` says it decodes slower than it plays. `maxBytes` is MaxClipBytes except in tests.
    public static VideoPlayer? Load(string folder, VideoLayer layer, List<string> warnings, SKSizeI canvas, VideoProbe probe,
        long maxBytes = MaxClipBytes)
    {
        var path = layer.Path;
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

        IVideoDecoder? decoder = null;
        try
        {
            decoder = VideoDecoders.Open(full);
            var scaler = new FrameScaler(layer.Fit, decoder.Size, SKRect.Create(canvas.Width, canvas.Height));
            if (TooSlow(decoder, scaler, probe) is { } slow)
            {
                decoder.Dispose();
                return Skip(warnings, path, slow);
            }

            decoder.Rewind();
            return new VideoPlayer(decoder, scaler); // owns the decoder from here
        }
        catch (Exception ex)
        {
            // A VideoDecodeException says what's wrong with the clip; anything else (a codec bug, say) is still just a
            // clip that can't play, not a theme that can't load.
            decoder?.Dispose();
            return Skip(warnings, path, ex is VideoDecodeException ? ex.Message : $"It could not be played: {ex.Message}");
        }
    }

    /// Why the clip can't keep up with itself, or null. A slow measurement is taken again, and the faster of the two
    /// counts, so a hiccup elsewhere on the PC doesn't cost a theme its background.
    static string? TooSlow(IVideoDecoder decoder, FrameScaler scaler, VideoProbe probe)
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

    static VideoPlayer? Skip(List<string> warnings, string path, string reason)
    {
        warnings.Add($"Video '{path}' was skipped: {reason}");
        return null;
    }
}
