using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Gauges drawn as dials: a needle at the value, ticks and labels inside the arc, and dials without an arc.
public class GaugeDialRenderingTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    static SKImage Render(string widgets, params (string, object)[] values) =>
        Frames.Render(Landscape + "widgets:\n" + widgets, TestSensors.Of(values));

    static SKColor At(SKImage image, float x, float y) => Images.PixelAt(image, (int)MathF.Round(x), (int)MathF.Round(y));

    /// Where a point `distance` from the centre (100, 100) of a 200 px dial at `degrees` lands.
    static (float X, float Y) Polar(float degrees, float distance) =>
        (100 + MathF.Cos(degrees * MathF.PI / 180) * distance, 100 + MathF.Sin(degrees * MathF.PI / 180) * distance);

    static SKColor At(SKImage image, (float X, float Y) point) => At(image, point.X, point.Y);

    /// A 200 px gauge at the canvas's corner, its value from gpu.temp, with the given extra keys.
    static string Gauge(string extra) => $"""
          - type: gauge
            at: [0, 0]
            size: [200, 200]
            source: gpu.temp
            {extra}
        """;

    const string Ring = "start: -90\n    sweep: 360\n    thickness: 0";

    [Fact]
    public void Needle_points_at_the_value()
    {
        using var frame = Render(Gauge(Ring + "\n    needle: { width: 16, length: 0.9, color: \"#FF0000\" }"), ("gpu.temp", 25));

        // A quarter of the way round from 12 o'clock is 3 o'clock; the tip is 90 px out.
        Pixels.AssertNear(SKColors.Red, At(frame, Polar(0, 40)));
        Pixels.AssertNear(SKColors.Red, At(frame, Polar(0, 70)));
        Pixels.AssertNear(SKColors.Black, At(frame, Polar(0, 94)), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, Polar(-90, 40)), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, Polar(90, 40)), 0);
    }

    [Theory]
    [InlineData(0, 135)]
    [InlineData(-50, 135)]
    [InlineData(100, 45)]
    [InlineData(250, 45)]
    public void Needle_rests_at_the_ends_of_the_default_arc_and_no_further(double temp, float degrees)
    {
        using var frame = Render(Gauge("needle: { width: 12, color: \"#FF0000\" }"), ("gpu.temp", temp));

        Pixels.AssertNear(SKColors.Red, At(frame, Polar(degrees, 30)));
        Pixels.AssertNear(SKColors.Black, At(frame, Polar(90, 30)), 0);
    }

    [Fact]
    public void Needle_is_drawn_over_the_arc()
    {
        using var frame = Render(Gauge("""
            start: -90
                sweep: 360
                thickness: 20
                needle: { width: 30, length: 1, color: "#FF0000" }
                style: { fill: "#00FF00", track: "#404040" }
            """), ("gpu.temp", 50));

        Pixels.AssertNear(SKColors.Red, At(frame, Polar(90, 85)));
        Pixels.AssertNear(SKColors.Lime, At(frame, Polar(45, 90)));
    }

    [Fact]
    public void Stale_needle_is_hidden_and_its_hub_stays()
    {
        using var frame = Render(Gauge(Ring + "\n    needle: { width: 12, cap: 10, color: \"#FF0000\" }"));

        Pixels.AssertNear(SKColors.Red, At(frame, 100, 100));
        // Parked at min, it would point straight up.
        Assert.Equal(0, Pixels.Count(frame, SKRectI.Create(0, 0, 200, 88), c => c.Red > 0));
    }

    [Fact]
    public void Needle_defaults_to_the_styles_color_which_a_rule_can_change()
    {
        var gauge = Gauge("""
            start: -90
                sweep: 360
                thickness: 0
                needle: { width: 12 }
                style: { color: "#00FF00" }
                when:
                  - { above: 50, style: { color: "#FF0000" } }
            """);
        using var cool = Render(gauge, ("gpu.temp", 25));
        using var hot = Render(gauge, ("gpu.temp", 75));

        Pixels.AssertNear(SKColors.Lime, At(cool, Polar(0, 40)));
        Pixels.AssertNear(SKColors.Red, At(hot, Polar(180, 40)));
    }

    [Fact]
    public void Needle_takes_the_styles_shadow_but_not_its_glow()
    {
        using var frame = Render(Gauge("""
            start: -90
                sweep: 360
                thickness: 0
                needle: { width: 8, length: 0.9, color: "#FF0000" }
                style: { shadow: { color: "#0000FF", x: 0, y: 8 }, glow: { color: "#00FF00", radius: 30 } }
            """), ("gpu.temp", 25));

        Pixels.AssertNear(SKColors.Red, At(frame, 130, 100));
        Pixels.AssertNear(SKColors.Blue, At(frame, 130, 108));
        Assert.Equal(0, Pixels.Count(frame, SKRectI.Create(0, 0, 200, 200), c => c.Green > 8));
    }

    [Fact]
    public void Smoothing_eases_the_needle()
    {
        using var folder = new TempTheme(Landscape + "widgets:\n" + Gauge(Ring + "\n    smooth: 1000\n    needle: { width: 16, color: \"#FF0000\" }"));
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme);

        using (renderer.Render(TestSensors.Of(("gpu.temp", 0)), TimeSpan.Zero)) { }
        using (renderer.Render(TestSensors.Of(("gpu.temp", 40)), TimeSpan.FromSeconds(1))) { }
        using var halfway = renderer.Render(TestSensors.Of(("gpu.temp", 40)), TimeSpan.FromSeconds(1.5));

        // Cubic ease-out is 87.5% of the way at half the duration: 35 of 100, 126 degrees round from 12 o'clock.
        Pixels.AssertNear(SKColors.Red, At(halfway, Polar(36, 40)));
        Pixels.AssertNear(SKColors.Black, At(halfway, Polar(54, 40)), 0);
    }

    [Fact]
    public void Thickness_0_draws_no_arc()
    {
        using var frame = Render(Gauge("thickness: 0\n    style: { fill: \"#FF0000\", track: \"#404040\" }"), ("gpu.temp", 100));

        Assert.Equal(0, Pixels.Count(frame, SKRectI.Create(0, 0, 200, 200), c => c.Red > 0 || c.Green > 0 || c.Blue > 0));
    }

    [Fact]
    public void Major_and_minor_ticks_sit_inside_the_arc()
    {
        using var frame = Render(Gauge("""
            start: -90
                sweep: 360
                thickness: 20
                ticks: { major: 4, minor: 1, length: 10, width: 4, color: "#FF0000" }
                style: { track: "#404040" }
            """), ("gpu.temp", 0));

        // The arc covers 80 to 100 px from the centre; a major tick 70 to 80, and a minor one 75 to 80.
        foreach (var degrees in new[] { -90f, 0, 90, 180 })
        {
            Pixels.AssertNear(SKColors.Red, At(frame, Polar(degrees, 72)));
            Pixels.AssertNear(new SKColor(0x40, 0x40, 0x40), At(frame, Polar(degrees, 90)));
        }

        Pixels.AssertNear(SKColors.Red, At(frame, Polar(45, 77)));
        Pixels.AssertNear(SKColors.Black, At(frame, Polar(45, 72)), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, Polar(22.5f, 77)), 0);
    }

    [Fact]
    public void Labels_sit_inside_the_ticks_in_the_styles_color()
    {
        using var frame = Render(Gauge(Ring + "\n    ticks: { major: 4, length: 10, color: \"#FF0000\", labels: \"{0:0}\" }\n    style: { size: 20, color: \"#00FF00\" }"),
            ("gpu.temp", 0));

        // 0, 25, 50 and 75 at 12, 3, 6 and 9 o'clock, just inside the ticks (90 to 100 px out); 100 would land on 0.
        foreach (var degrees in new[] { -90f, 0, 90, 180 })
        {
            var (x, y) = Polar(degrees, 72);
            Assert.True(Pixels.Count(frame, SKRectI.Create((int)x - 25, (int)y - 25, 50, 50), c => c.Green > 100) > 10, $"no label at {degrees}°");
        }

        Assert.Equal(0, Pixels.Count(frame, SKRectI.Create(60, 60, 80, 80), c => c.Green > 100));
        foreach (var edge in new[] { SKRectI.Create(0, 0, 200, 12), SKRectI.Create(0, 188, 200, 12), SKRectI.Create(0, 0, 12, 200), SKRectI.Create(188, 0, 12, 200) })
            Assert.Equal(0, Pixels.Count(frame, edge, c => c.Green > 0));
    }

    /// The green ink's width in the top quarter of the dial, where the label at 12 o'clock sits.
    static int TopLabelWidth(SKImage frame)
    {
        var (left, right) = (int.MaxValue, int.MinValue);
        for (var x = 0; x < 200; x++)
            if (Pixels.Count(frame, SKRectI.Create(x, 0, 1, 50), c => c.Green > 100) > 0)
                (left, right) = (Math.Min(left, x), Math.Max(right, x + 1));
        return right - left;
    }

    [Fact]
    public void Labels_take_the_styles_letter_spacing()
    {
        static int Width(string spacing)
        {
            using var frame = Render(Gauge(Ring + "\n    ticks: { major: 4, length: 10, color: \"#FF0000\", labels: \"{0:000}\" }\n"
                + $"    style: {{ size: 20, color: \"#00FF00\", letter_spacing: {spacing} }}"), ("gpu.temp", 0));
            return TopLabelWidth(frame);
        }

        // "000" has two gaps between its characters.
        Assert.InRange(Width("10") - Width("0"), 18, 22);
    }

    [Fact]
    public void A_label_that_would_overlap_the_one_before_is_left_out()
    {
        using var frame = Render(Gauge("start: -100\n    sweep: 20\n    thickness: 0\n"
            + "    ticks: { major: 1, length: 6, color: \"#FF0000\", labels: \"{0:000}\" }\n    style: { size: 20, color: \"#00FF00\" }"),
            ("gpu.temp", 0));

        // 000 sits just left of 12 o'clock and 100 just right of it, closer together than either is wide: only 000 shows.
        Assert.True(Pixels.Count(frame, SKRectI.Create(60, 0, 44, 50), c => c.Green > 100) > 10);
        Assert.Equal(0, Pixels.Count(frame, SKRectI.Create(108, 0, 40, 50), c => c.Green > 100));
    }

    [Fact]
    public void Ticks_without_labels_draw_no_text()
    {
        using var frame = Render(Gauge(Ring + "\n    ticks: { major: 4, color: \"#FF0000\", labels: false }\n    style: { color: \"#00FF00\" }"),
            ("gpu.temp", 0));

        Assert.Equal(0, Pixels.Count(frame, SKRectI.Create(0, 0, 200, 200), c => c.Green > 8));
        Assert.True(Pixels.Count(frame, SKRectI.Create(0, 0, 200, 200), c => c.Red > 200) > 20);
    }

    [Fact]
    public void Dials_snapshot()
    {
        var store = TestSensors.Store(("cpu.temp", 64), ("gpu.temp", 0), ("gpu.load", 100), ("cpu.load", 37), ("fan.rpm", 2400));

        using var frame = Frames.Render(Landscape + """
            styles:
              face: { size: 22, weight: medium, color: "#C8D0E0", track: "#FFFFFF18", fill: { linear: ["#36D1DC", "#FF5A36"] } }
            background:
              - fill: "#0B0F1A"
            widgets:
              # Arc, ticks, labels and a needle with a hub.
              - type: gauge
                at: [20, 60]
                size: [360, 360]
                source: cpu.temp
                min: 20
                max: 100
                thickness: 16
                needle: { width: 10, length: 0.75, color: "#FF5A36", cap: 16 }
                ticks: { major: 8, minor: 4, length: 16, width: 2, color: "#FFFFFF90", labels: "{0:0}" }
                style: face
              # The needle at min, with ticks but no labels.
              - type: gauge
                at: [400, 60]
                size: [360, 360]
                source: gpu.temp
                min: 20
                max: 100
                thickness: 10
                needle: { width: 8, cap: 10 }
                ticks: { major: 10, minor: 1, length: 14, color: "#FFFFFF60", labels: false }
                style: face
              # The needle at max, and labels with units.
              - type: gauge
                at: [780, 60]
                size: [360, 360]
                source: gpu.load
                thickness: 24
                needle: { width: 12, length: 0.6, color: "#F4F6FA", cap: 20 }
                ticks: { major: 4, length: 10, width: 3, labels: "{0:0}%" }
                style: { extends: face, stroke: { cap: round } }
              # No arc: a full-circle dial whose last label would land on its first.
              - type: gauge
                at: [1160, 60]
                size: [360, 360]
                source: cpu.load
                start: -90
                sweep: 360
                thickness: 0
                needle: { width: 6, length: 0.85, color: "#FF5A36", cap: 8 }
                ticks: { major: 12, minor: 4, length: 18, width: 3, labels: "{0:0}" }
                style: { extends: face, shadow: { color: "#000000C0", x: 3, y: 3, blur: 4 } }
              # A needle and nothing else.
              - type: gauge
                at: [1540, 60]
                size: [360, 360]
                source: fan.rpm
                max: 3000
                thickness: 0
                needle: { width: 14, length: 0.95, cap: 24 }
                style: { color: "#36D1DC", shadow: { color: "#000000C0", x: 4, y: 4, blur: 6 } }
            """, store.Snapshot());

        Snapshots.Match(frame, "dials");
    }
}
