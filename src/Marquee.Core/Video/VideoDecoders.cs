namespace Marquee.Core.Video;

/// Picks the decoder for a clip by its file extension, the one thing layout.yaml checks about it.
public static class VideoDecoders
{
    /// Whether Media Foundation clips may decode on the graphics card (see MediaFoundationDecoder). The graphics card's
    /// colour conversion differs slightly from Windows' software one at sharp colour edges, so tests that compare
    /// pixels set this to false once at startup, for the same pixels on every machine, with or without a GPU.
    public static bool UseHardware { get; set; } = true;

    /// Opens the clip: GIF and WebP through Skia, everything else (MP4, M4V, MOV, WMV) through Media Foundation.
    /// Errors are VideoDecodeException with a message that gives the cause for the user; the caller names the file.
    public static IVideoDecoder Open(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".gif" or ".webp"
            ? new SkiaAnimatedDecoder(path)
            : new MediaFoundationDecoder(path, UseHardware);
}
