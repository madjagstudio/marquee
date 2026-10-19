using SkiaSharp;

namespace Marquee.Core.Video;

/// The buffers a clip's frames are scaled into, reused rather than allocated for every frame: a new canvas-sized frame
/// each time is 3.7 MB at 1920×480, so about 110 MB a second of native memory per 30 fps layer for the allocator to churn.
///
/// Each frame is an image that wraps its buffer without copying it. A buffer is reused only once Skia frees that image,
/// which it does when the last reference to it goes: the queue's, and the render thread's once VideoPlayer's ownership
/// rules have it release the frame after drawing it. So a buffer is never written while anything can still draw it,
/// whoever holds the image and for however long. Thread-safe: images are released on the render thread, the decode
/// thread and the disposing one.
sealed class FrameBuffers(FrameScaler scaler) : IDisposable
{
    /// Free buffers kept for reuse. A player holds at most FramesAhead + 2 frames at once (queued, on screen, being
    /// decoded), so its free buffers rarely number more than that; any past it are freed rather than kept.
    const int MaxFree = VideoPlayer.FramesAhead + 2;

    readonly object _gate = new();
    readonly Stack<SKBitmap> _free = new();
    bool _disposed;
    int _created;

    /// Buffers allocated so far.
    internal int Created
    {
        get
        {
            lock (_gate) return _created;
        }
    }

    /// The frame scaled into a free buffer, as an image of the scaler's Destination size. Disposing the image (its last
    /// reference) gives the buffer back. The pixels are only read during the call.
    public SKImage Scale(SKPixmap pixels)
    {
        var buffer = Take();
        try
        {
            scaler.Scale(pixels, buffer);
        }
        catch
        {
            Return(buffer);
            throw;
        }

        using var pixmap = buffer.PeekPixels();
        // Skia calls this once, when it frees the image, on whichever thread lets go of it last. If it can't make the
        // image it never calls it, and the buffer is left to its finalizer rather than risk returning it twice.
        return SKImage.FromPixels(pixmap, (_, _) => Return(buffer))
            ?? throw new InvalidOperationException("Could not wrap a scaled video frame.");
    }

    /// Frees the buffers not in use. Each one still wrapped by an image is freed when that image is.
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var buffer in _free) buffer.Dispose();
            _free.Clear();
        }
    }

    SKBitmap Take()
    {
        lock (_gate)
        {
            if (_free.TryPop(out var buffer)) return buffer;
            _created++;
        }

        var size = scaler.Destination.Size;
        return new SKBitmap(new SKImageInfo(size.Width, size.Height));
    }

    void Return(SKBitmap buffer)
    {
        lock (_gate)
        {
            if (!_disposed && _free.Count < MaxFree)
            {
                _free.Push(buffer);
                return;
            }
        }

        buffer.Dispose();
    }
}
