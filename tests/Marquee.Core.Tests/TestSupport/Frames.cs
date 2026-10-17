using Marquee.Core.Devices;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Tests.TestSupport;

/// Renders frames the way the app does and reads back what a FileSinkDevice wrote, so tests see the real output.
static class Frames
{
    public static SKImage Render(string layout, SensorSnapshot sensors, TimeSpan time = default)
    {
        using var folder = new TempTheme(layout);
        using var theme = folder.Load();
        return Render(theme, sensors, time);
    }

    public static SKImage Render(LoadedTheme theme, SensorSnapshot sensors, TimeSpan time = default)
    {
        using var renderer = new ThemeRenderer(theme);
        using var frame = renderer.Render(sensors, time);
        return ThroughSink(frame);
    }

    public static SKImage ThroughSink(SKImage frame)
    {
        var folder = Path.Combine(Path.GetTempPath(), "marquee-frames-" + Guid.NewGuid().ToString("N")[..8]);
        using (var sink = new FileSinkDevice(folder, frame.Width, frame.Height))
        {
            sink.Open();
            sink.PushFrame(frame);
        }

        var png = File.ReadAllBytes(Path.Combine(folder, "frame_000001.png"));
        Directory.Delete(folder, recursive: true);
        return SKImage.FromEncodedData(png);
    }
}
