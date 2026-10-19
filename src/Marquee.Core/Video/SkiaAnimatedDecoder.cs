using SkiaSharp;

namespace Marquee.Core.Video;

/// Plays an animated GIF or WebP through Skia's codecs. Each frame is decoded into the same full-size bitmap, over the
/// frame before it, because these formats store a frame as a change to the earlier picture: often only a rectangle of
/// it, to be drawn over what was kept. The file's own loop count is ignored: video layers always loop.
public sealed class SkiaAnimatedDecoder : IVideoDecoder
{
    /// Browsers play a delay of 10 ms or less as 100 ms, and GIFs are made to look right in browsers: many say 0 and
    /// rely on it, and played as written they would race through.
    static readonly TimeSpan MaxShortDelay = TimeSpan.FromMilliseconds(10);
    static readonly TimeSpan ShortDelayPlaysAs = TimeSpan.FromMilliseconds(100);

    /// Skia's "no frame": a frame that needs no earlier one, or a decode that has no earlier frame to start from.
    const int NoFrame = -1;

    readonly SKCodec _codec;
    readonly SKCodecFrameInfo[] _frames;
    readonly SKBitmap _bitmap;
    readonly SKPixmap _pixmap;
    int _next;
    TimeSpan _start;

    public SkiaAnimatedDecoder(string path)
    {
        _codec = Open(path);
        try
        {
            var info = _codec.Info;
            VideoLimits.Check(info.Width, info.Height);

            // Empty for an image that isn't animated, such as a still WebP, which plays as a single frame.
            _frames = _codec.FrameInfo;
            _bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            _pixmap = _bitmap.PeekPixels();
        }
        catch
        {
            _bitmap?.Dispose();
            _codec.Dispose();
            throw;
        }
    }

    public SKSizeI Size => new(_bitmap.Width, _bitmap.Height);

    int FrameCount => Math.Max(_frames.Length, 1);

    public DecodedFrame? Next()
    {
        if (_next >= FrameCount)
            return null;

        Decode(_next);
        var duration = Duration(_next);
        var frame = new DecodedFrame(_pixmap, _start, duration);
        _next++;
        _start += duration;
        return frame;
    }

    public void Rewind()
    {
        _next = 0;
        _start = TimeSpan.Zero;
    }

    public void Dispose()
    {
        _pixmap.Dispose();
        _bitmap.Dispose();
        _codec.Dispose();
    }

    void Decode(int index)
    {
        // With a prior frame, Skia draws this frame over the bitmap as it stands, so the bitmap must hold exactly that
        // frame. Frames are read in order, so it holds the one before this. That frame can be the prior unless it is
        // disposed by restoring the frame before it, which Skia can't undo in place; then Skia rebuilds the picture
        // from the frame this one needs, decoding that again.
        var prior = NoFrame;
        if (_frames.Length > 0 && _frames[index].RequiredFrame != NoFrame
            && _frames[index - 1].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious)
            prior = index - 1;

        var result = _codec.GetPixels(_bitmap.Info, _bitmap.GetPixels(), new SKCodecOptions(index, prior));

        // A damaged or truncated frame still decodes as far as it goes, the way browsers show it.
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput or SKCodecResult.ErrorInInput))
            throw new VideoDecodeException($"Frame {index + 1} of the clip could not be decoded ({result}).");
    }

    TimeSpan Duration(int index)
    {
        // An image that isn't animated has no delay of its own; it shows like a GIF frame with none.
        if (_frames.Length == 0)
            return ShortDelayPlaysAs;
        var delay = TimeSpan.FromMilliseconds(_frames[index].Duration);
        return delay <= MaxShortDelay ? ShortDelayPlaysAs : delay;
    }

    static SKCodec Open(string path)
    {
        if (!File.Exists(path))
            throw new VideoDecodeException("The clip was not found.");

        // The codec reads the file as it decodes, through a stream it owns and closes with itself (see ClipFile).
        var file = ClipFile.Open(path);
        var codec = SKCodec.Create(file);
        if (codec is null)
        {
            file.Dispose();
            throw new VideoDecodeException("The clip is not a GIF or WebP image that can be read.");
        }

        if (codec.EncodedFormat is not (SKEncodedImageFormat.Gif or SKEncodedImageFormat.Webp))
        {
            var format = codec.EncodedFormat.ToString().ToUpperInvariant();
            codec.Dispose();
            throw new VideoDecodeException($"The clip is in {format} format, not GIF or WebP.");
        }

        return codec;
    }
}
