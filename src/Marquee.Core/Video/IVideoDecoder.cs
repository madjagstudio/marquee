using SkiaSharp;

namespace Marquee.Core.Video;

/// A clip decoded frame by frame, in order. Not thread-safe: one thread reads it.
public interface IVideoDecoder : IDisposable
{
    SKSizeI Size { get; }

    /// The next frame, or null at the end of the clip. Its pixels belong to the decoder and are valid only until the next call.
    DecodedFrame? Next();

    /// Back to the first frame.
    void Rewind();
}

/// One frame of a clip: its pixels, when it starts from the beginning of the clip, and how long it shows.
public readonly record struct DecodedFrame(SKPixmap Pixels, TimeSpan Start, TimeSpan Duration);
