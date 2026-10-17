using Marquee.Core.Devices;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class FileSinkDeviceTests
{
    [Fact]
    public void PushFrame_writes_numbered_pngs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marquee-sink-" + Guid.NewGuid());
        using var sink = new FileSinkDevice(dir);
        sink.Open();

        sink.PushFrame(Images.Landscape(SKColors.Red));
        sink.PushFrame(Images.Landscape(SKColors.Red));

        Assert.Equal(
            new[] { "frame_000001.png", "frame_000002.png" },
            Directory.GetFiles(dir).Select(f => Path.GetFileName(f)).Order().ToArray());
        using var first = SKImage.FromEncodedData(Path.Combine(dir, "frame_000001.png"));
        Assert.Equal(1920, first.Width);
        Directory.Delete(dir, recursive: true);
    }
}
