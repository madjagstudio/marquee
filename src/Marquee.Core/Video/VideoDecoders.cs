namespace Marquee.Core.Video;

/// Picks the decoder for a clip by its file extension, the one thing layout.yaml checks about it.
public static class VideoDecoders
{
    /// Opens the clip: GIF and WebP through Skia, everything else (MP4, M4V, MOV, WMV) through Media Foundation.
    /// Errors are VideoDecodeException with a message that gives the cause for the user; the caller names the file.
    public static IVideoDecoder Open(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".gif" or ".webp"
            ? new SkiaAnimatedDecoder(path)
            : new MediaFoundationDecoder(path);
}
