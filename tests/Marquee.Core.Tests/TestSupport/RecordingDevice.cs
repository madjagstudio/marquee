using Marquee.Core.Devices;
using SkiaSharp;

namespace Marquee.Core.Tests.TestSupport;

/// A device that keeps a copy of the last frame and can be told to fail on a given Open or PushFrame attempt (1-based).
sealed class RecordingDevice(int width = 1920, int height = 480) : IDisplayDevice
{
    public DeviceInfo Info { get; } = new("Recording device", width, height);

    public Func<int, Exception?> OpenFault { get; set; } = _ => null;
    public Func<int, Exception?> PushFault { get; set; } = _ => null;

    public int Opens { get; private set; }
    public int Closes { get; private set; }
    public int PushAttempts { get; private set; }
    public int FramesPushed { get; private set; }
    public SKBitmap? LastFrame { get; private set; }

    public void Open()
    {
        Opens++;
        if (OpenFault(Opens) is { } fault) throw fault;
    }

    public void PushFrame(SKImage frame)
    {
        PushAttempts++;
        if (PushFault(PushAttempts) is { } fault) throw fault;
        FramesPushed++;
        LastFrame?.Dispose();
        LastFrame = SKBitmap.FromImage(frame);
    }

    public void SetBrightness(int percent) { }

    public void Close() => Closes++;

    public void Dispose() => LastFrame?.Dispose();
}
