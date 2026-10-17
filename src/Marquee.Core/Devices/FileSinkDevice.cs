using SkiaSharp;

namespace Marquee.Core.Devices;

/// Writes each frame as a numbered PNG, for development and CI without hardware.
public sealed class FileSinkDevice(string directory, int width = 1920, int height = 480) : IDisplayDevice
{
    int _frame;

    public DeviceInfo Info { get; } = new("File sink", width, height);

    public void Open() => Directory.CreateDirectory(directory);

    public void PushFrame(SKImage frame)
    {
        _frame++;
        using var data = frame.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, $"frame_{_frame:D6}.png"), data.ToArray());
    }

    public void SetBrightness(int percent) { }

    public void Close() { }

    public void Dispose() { }
}
