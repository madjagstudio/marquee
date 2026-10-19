using Marquee.Core.Devices;
using SkiaSharp;

namespace Marquee.Host.Tests.TestSupport;

/// A screen that counts what happens to it and keeps the color of one pixel of the last frame. The render loop's
/// sending thread drives it while the test thread reads it, so the counts are interlocked.
sealed class RecordingDevice(int width = 1920, int height = 480) : IDisplayDevice
{
    int _opens;
    int _closes;
    int _framesPushed;
    int _disposed;
    uint _lastPixel;

    public DeviceInfo Info { get; } = new("Recording device", width, height);

    public int Opens => Volatile.Read(ref _opens);
    public int Closes => Volatile.Read(ref _closes);
    public int FramesPushed => Volatile.Read(ref _framesPushed);
    public bool Disposed => Volatile.Read(ref _disposed) > 0;

    /// The pixel at (10, 10) of the last frame pushed.
    public SKColor LastPixel => (SKColor)Volatile.Read(ref _lastPixel);

    public void Open() => Interlocked.Increment(ref _opens);

    public void PushFrame(SKImage frame)
    {
        using var bitmap = SKBitmap.FromImage(frame);
        Volatile.Write(ref _lastPixel, (uint)bitmap.GetPixel(10, 10));
        Interlocked.Increment(ref _framesPushed);
    }

    public void SetBrightness(int percent) { }

    public void Close() => Interlocked.Increment(ref _closes);

    public void Dispose() => Interlocked.Increment(ref _disposed);
}
