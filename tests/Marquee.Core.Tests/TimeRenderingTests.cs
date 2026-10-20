using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Clocks drawn by the renderer, with the clock pinned through the test sensors (Friday 16 October 2026, 12:00 UTC)
/// and the renderer's zone and culture pinned to UTC+05:30 and US English, so the frames match on every machine.
public class TimeRenderingTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    static SKImage Render(string layout, SensorSnapshot sensors)
    {
        using var folder = new TempTheme(layout);
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme) { Locale = TestClock.Locale() };
        using var frame = renderer.Render(sensors, TimeSpan.Zero);
        return Frames.ThroughSink(frame);
    }

    [Fact]
    public void Clock_texts()
    {
        using var frame = Render(Landscape + """
            background:
              - fill: { linear: ["#0B0F1A", "#1B2440"], angle: 90 }
            widgets:
              - { type: text, at: [40, 40], size: [420, 160], text: "{now|time:HH:mm}", style: { size: 128, weight: bold, color: "#F4F6FA" } }
              - { type: text, at: [500, 40], size: [520, 160], text: "{now|time:h:mm tt}", style: { size: 96, color: "#36D1DC" } }
              - { type: text, at: [1060, 40], size: [820, 160], text: "{now|time:dddd}", style: { size: 96, color: "#FFB547", align: right } }
              - { type: text, at: [40, 260], size: [1840, 120], text: "{now|time:D}", style: { size: 72, color: "#E0E6F0", align: center } }
            """, TestSensors.Of());

        Snapshots.Match(frame, "time-clock-texts");
    }

    [Fact]
    public void Value_widgets_with_a_time_format()
    {
        using var frame = Render(Landscape + """
            background:
              - fill: "#101521"
            widgets:
              - { type: value, at: [40, 40], size: [600, 160], source: now, format: "time:HH:mm:ss", style: { size: 112, color: "#F4F6FA" } }
              - type: value
                at: [700, 40]
                size: [1180, 160]
                source: claude.five_hour.resets_at
                format: "time:ddd h:mm tt"
                style: { size: 96, color: "#36D1DC", align: right }
              - type: value
                at: [700, 260]
                size: [1180, 160]
                source: claude.seven_day.resets_at
                format: "time:ddd h:mm tt"
                style: { size: 96, color: "#36D1DC", align: right }
                when:
                  - { stale: true, style: { color: "#5A6275", fill: "#40202080", radius: 12, padding: 24 } }
            """, TestSensors.Of(("claude.five_hour.resets_at", TestSensors.UnixTime + 8040)));

        Snapshots.Match(frame, "time-value-widgets");
    }

    static void AssertSamePixels(SKImage expected, SKImage actual)
    {
        using var a = SKBitmap.FromImage(expected);
        using var b = SKBitmap.FromImage(actual);
        Assert.True(a.Pixels.SequenceEqual(b.Pixels), "The two frames differ");
    }

    // The dial has no value, so no needle or arc: only its face and tick labels are drawn.
    const string Dial = "  - { type: gauge, at: [40, 40], size: [400, 400], source: cpu.load, thickness: 0, style: { size: 32 }, ";

    [Fact]
    public void Dial_tick_labels_write_times_in_the_renderers_zone()
    {
        // 00:00, 05:00 and 10:00 UTC on 1 January 1970 are 05:30, 10:30 and 15:30 in the pinned zone.
        using var times = Render(Landscape + "widgets:\n" + Dial
            + "min: 0, max: 36000, ticks: { major: 2, labels: \"time:HH:mm\" } }\n", TestSensors.Of());
        using var written = Render(Landscape + "widgets:\n" + Dial
            + "min: 5, max: 15, ticks: { major: 2, labels: \"{0:00}:30\" } }\n", TestSensors.Of());

        AssertSamePixels(written, times);
    }

    [Fact]
    public void Donut_shares_write_times_in_the_renderers_zone()
    {
        // A slice alone, beside one of 0 that is left out, has a share of 100, read here as 100 seconds after midnight
        // UTC: 05:31 in the pinned zone.
        var sensors = TestSensors.Of(("cpu.power", 50), ("gpu.power", 0));
        const string Donut = "  - { type: donut, at: [40, 40], size: [400, 400], hole: 0.3, labels: inside, style: { size: 32 }, ";
        using var times = Render(Landscape + "widgets:\n" + Donut
            + "format: \"time:HH:mm\", slices: [{ source: cpu.power, label: at, style: { fill: \"#2A8C94\" } }, { source: gpu.power }] }\n", sensors);
        using var written = Render(Landscape + "widgets:\n" + Donut
            + "slices: [{ source: cpu.power, label: \"at 05:31\", style: { fill: \"#2A8C94\" } }, { source: gpu.power }] }\n", sensors);

        AssertSamePixels(written, times);
    }
}
