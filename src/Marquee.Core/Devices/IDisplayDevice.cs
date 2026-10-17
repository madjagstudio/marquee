using SkiaSharp;

namespace Marquee.Core.Devices;

/// Name and logical canvas size. Frames are drawn at this size, and the device handles its own orientation.
public sealed record DeviceInfo(string Name, int Width, int Height);

public interface IDisplayDevice : IDisposable
{
    DeviceInfo Info { get; }
    void Open();
    void PushFrame(SKImage frame);
    void SetBrightness(int percent);
    void Close();
}
