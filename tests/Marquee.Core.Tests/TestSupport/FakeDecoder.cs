using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests.TestSupport;

/// A clip of solid-color frames, each `frameMs` long, for testing what plays a clip without decoding one. Frames from
/// `blockFrom` on wait for Release; the frame at `failAt` throws as a damaged one would. It records the thread that
/// reads it and how often it is read.
sealed class FakeDecoder : IVideoDecoder
{
    readonly SKColor[] _colors;
    readonly TimeSpan _frameLength;
    readonly SKBitmap _bitmap;
    readonly SKPixmap _pixels;
    readonly ManualResetEventSlim _released = new(false);
    int _next;
    int _reads;

    public FakeDecoder(int frameMs, params SKColor[] colors)
    {
        _colors = colors;
        _frameLength = TimeSpan.FromMilliseconds(frameMs);
        _bitmap = new SKBitmap(new SKImageInfo(Size.Width, Size.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        _pixels = _bitmap.PeekPixels();
    }

    public SKSizeI Size => new(4, 4);

    /// Frames from this index on wait until Release is called.
    public int BlockFrom { get; init; } = int.MaxValue;

    /// The frame at this index throws a VideoDecodeException.
    public int FailAt { get; init; } = -1;

    /// Real time each Next takes, for cost measurements.
    public TimeSpan Delay { get; init; }

    /// The first frame's time stamp; the others follow it `frameMs` apart. Real clips don't always start at zero.
    public int FirstMs { get; init; }

    public int Reads => Volatile.Read(ref _reads);
    public int Rewinds { get; private set; }
    public Thread? ReadBy { get; private set; }
    public bool IsDisposed { get; private set; }

    public void Release() => _released.Set();

    public DecodedFrame? Next()
    {
        ReadBy = Thread.CurrentThread;
        Interlocked.Increment(ref _reads);
        if (_next >= _colors.Length) return null;
        if (_next >= BlockFrom) _released.Wait(TimeSpan.FromSeconds(30));
        if (_next == FailAt) throw new VideoDecodeException($"Frame {_next + 1} is damaged.");
        if (Delay > TimeSpan.Zero) Thread.Sleep(Delay);

        _bitmap.Erase(_colors[_next]);
        var frame = new DecodedFrame(_pixels, TimeSpan.FromMilliseconds(FirstMs) + _frameLength * _next, _frameLength);
        _next++;
        return frame;
    }

    public void Rewind()
    {
        Rewinds++;
        _next = 0;
    }

    public void Dispose()
    {
        IsDisposed = true;
        _released.Set(); // nothing may stay blocked in Next once the test is done with it
        _pixels.Dispose();
        _bitmap.Dispose();
        _released.Dispose();
    }
}
