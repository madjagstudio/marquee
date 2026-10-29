using SkiaSharp;

namespace Marquee.Core.Devices;

/// Remembers the last frame a device sent, so it can skip sending an identical one. Converting, encoding and sending a
/// frame costs more CPU than drawing most themes, and a dashboard whose values change once a second would otherwise
/// send the same picture 30 times a second. An identical frame still goes out every RefreshEvery, so a screen that
/// dropped one picks the picture up again. Not thread-safe: the device's own thread uses it.
public sealed class RepeatedFrames(TimeProvider? time = null)
{
    /// How often a frame identical to the last one is sent again.
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(1);

    readonly TimeProvider _time = time ?? TimeProvider.System;
    byte[] _pixels = [];
    SKImageInfo _info;
    int _rowBytes;
    long? _sentAt; // a timestamp of _time, or null when nothing is remembered

    /// True when `frame` shows the same pixels as the last frame Sent, and that went out less than RefreshEvery ago. A
    /// frame whose pixels can't be read without a copy (not a raster image) is never a repeat.
    public bool IsRepeat(SKImage frame)
    {
        if (_sentAt is not { } sentAt || _time.GetElapsedTime(sentAt) >= RefreshEvery) return false;
        using var pixmap = frame.PeekPixels();
        return pixmap is not null && pixmap.Info == _info && pixmap.RowBytes == _rowBytes &&
            pixmap.GetPixelSpan().SequenceEqual(_pixels);
    }

    /// Records `frame` as the one the screen now shows.
    public void Sent(SKImage frame)
    {
        using var pixmap = frame.PeekPixels();
        if (pixmap is null)
        {
            Forget();
            return;
        }

        var pixels = pixmap.GetPixelSpan();
        if (_pixels.Length != pixels.Length) _pixels = new byte[pixels.Length];
        pixels.CopyTo(_pixels);
        _info = pixmap.Info;
        _rowBytes = pixmap.RowBytes;
        _sentAt = _time.GetTimestamp();
    }

    /// Forgets the last frame, so the next one is sent whatever it shows: for a screen that was reopened or cleared.
    public void Forget() => _sentAt = null;
}
