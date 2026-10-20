using Marquee.Core.Rendering;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Bars and gauges cut into segments, like a VU meter: `segments` and `gap` (B5).
public class SegmentedWidgetTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";
    const string Header = Landscape + "widgets:\n";
    static readonly SKColor Track = new(0x40, 0x40, 0x40);

    static IReadOnlyList<WidgetSpec> Widgets(string widgets) => Layouts.Parse(Header + widgets).Widgets;

    /// Line numbers in errors count from the top of the file; the header is 2 lines long.
    static ThemeException Error(string widgets) => Layouts.Error(Header + widgets);

    static SKImage Render(string widgets, params (string, object)[] values) =>
        Frames.Render(Header + widgets, TestSensors.Of(values));

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    /// A point on a 200 px gauge's ring at `degrees` clockwise from 3 o'clock, 90 px from the centre (100, 100).
    static SKColor OnRing(SKImage image, float degrees)
    {
        var radians = degrees * MathF.PI / 180;
        return At(image, (int)MathF.Round(100 + 90 * MathF.Cos(radians)), (int)MathF.Round(100 + 90 * MathF.Sin(radians)));
    }

    // ---- Parsing ----

    [Fact]
    public void Segments_and_gap_parse_with_a_default_gap_for_each_type()
    {
        var widgets = Widgets("""
            - { type: bar, at: [0, 0], size: [400, 40], source: x, segments: 10 }
            - { type: bar, at: [0, 0], size: [400, 40], source: x, segments: 20, gap: 2.5 }
            - { type: gauge, at: [0, 0], size: [200, 200], source: x, segments: 24 }
            - { type: gauge, at: [0, 0], size: [200, 200], source: x, segments: 12, gap: 0 }
            """);

        Assert.Equal(new SegmentSpec(10, 4), Assert.IsType<BarWidget>(widgets[0]).Segments);
        Assert.Equal(new SegmentSpec(20, 2.5f), Assert.IsType<BarWidget>(widgets[1]).Segments);
        Assert.Equal(new SegmentSpec(24, 3), Assert.IsType<GaugeWidget>(widgets[2]).Segments);
        Assert.Equal(new SegmentSpec(12, 0), Assert.IsType<GaugeWidget>(widgets[3]).Segments);
    }

    [Fact]
    public void Without_segments_a_bar_or_gauge_is_continuous_and_a_gap_alone_changes_nothing()
    {
        var widgets = Widgets("""
            - { type: bar, at: [0, 0], size: [400, 40], source: x }
            - { type: bar, at: [0, 0], size: [400, 40], source: x, gap: 6 }
            - { type: gauge, at: [0, 0], size: [200, 200], source: x, gap: 6 }
            """);

        Assert.Null(Assert.IsType<BarWidget>(widgets[0]).Segments);
        Assert.Equal(widgets[0], widgets[1]);
        Assert.Null(Assert.IsType<GaugeWidget>(widgets[2]).Segments);
    }

    [Theory]
    [InlineData("horizontal", "[400, 40]", 80)]
    [InlineData("vertical", "[40, 400]", 80)]
    public void A_bar_takes_as_many_segments_as_fit_at_1_px_each(string direction, string size, int most)
    {
        // 80 segments of 1 px and 79 gaps of 4 px make 396 px; 81 would need 401.
        var bar = Assert.IsType<BarWidget>(Assert.Single(Widgets(
            $"- {{ type: bar, at: [0, 0], size: {size}, source: x, direction: {direction}, segments: {most} }}")));

        Assert.Equal(most, bar.Segments!.Count);
    }

    [Theory]
    [InlineData(270, 68)]
    [InlineData(-270, 68)]
    [InlineData(360, 90)]
    public void A_gauge_takes_as_many_segments_as_fit_at_1_degree_each(int sweep, int most)
    {
        // An arc has a gap between each pair of segments: 68 + 67 × 3 = 269°. A full ring also has one at the join:
        // 90 + 90 × 3 = 360°.
        var gauge = Assert.IsType<GaugeWidget>(Assert.Single(Widgets(
            $"- {{ type: gauge, at: [0, 0], size: [200, 200], source: x, sweep: {sweep}, segments: {most} }}")));

        Assert.Equal(most, gauge.Segments!.Count);
    }

    [Fact]
    public void The_most_segments_fit_with_no_gap()
    {
        var widgets = Widgets("""
            - { type: bar, at: [0, 0], size: [120, 10], source: x, segments: 120, gap: 0 }
            - { type: gauge, at: [0, 0], size: [10, 10], source: x, sweep: 120, segments: 120, gap: 0 }
            """);

        Assert.Equal(120, Assert.IsType<BarWidget>(widgets[0]).Segments!.Count);
        Assert.Equal(120, Assert.IsType<GaugeWidget>(widgets[1]).Segments!.Count);
    }

    [Theory]
    [InlineData("segments: 1", "segments must be between 2 and 120")]
    [InlineData("segments: 121", "segments must be between 2 and 120")]
    [InlineData("segments: 2.5", "segments must be a whole number")]
    [InlineData("segments: many", "segments must be a number, not 'many'")]
    [InlineData("segments: 10, gap: -1", "gap must be at least 0")]
    [InlineData("gap: -1", "gap must be at least 0")]
    public void Segment_ranges(string keys, string reason)
    {
        var ex = Error($"- {{ type: bar, at: [0, 0], size: [400, 40], source: x, {keys} }}");

        Assert.Equal(reason, ex.Reason);
    }

    [Theory]
    [InlineData("bar", "size: [400, 40]", "segments: 81", null, 7,
        "81 segments of at least 1 px with 4 px gaps need 401 px, but this bar is 400 px long; use fewer segments or a smaller gap")]
    [InlineData("bar", "size: [40, 300]\n  direction: vertical", "segments: 52", "gap: 5", 9,
        "52 segments of at least 1 px with 5 px gaps need 307 px, but this bar is 300 px long; use fewer segments or a smaller gap")]
    [InlineData("bar", "size: [40, 300]\n  direction: vertical", "segments: 50", "gap: 5.5", 9,
        "50 segments of at least 1 px with 5.5 px gaps need 319.5 px, but this bar is 300 px long; use fewer segments or a smaller gap")]
    [InlineData("gauge", "size: [200, 200]", "segments: 69", null, 7,
        "69 segments of at least 1° with 3° gaps need 273°, but this gauge sweeps 270°; use fewer segments or a smaller gap")]
    [InlineData("gauge", "size: [200, 200]\n  sweep: -180", "segments: 10", "gap: 20", 9,
        "10 segments of at least 1° with 20° gaps need 190°, but this gauge sweeps 180°; use fewer segments or a smaller gap")]
    [InlineData("gauge", "size: [200, 200]\n  sweep: 360", "segments: 91", null, 8,
        "91 segments of at least 1° with 3° gaps need 364° (a full ring has a gap at the join too), but this gauge sweeps 360°; use fewer segments or a smaller gap")]
    public void Gaps_that_leave_no_room_for_the_segments_are_an_error_on_the_gap(string type, string shape, string segments,
        string? gap, int line, string reason)
    {
        var ex = Error($"""
            - type: {type}
              at: [0, 0]
              {shape}
              source: x
              {segments}
            {(gap is null ? "" : "  " + gap)}
            """);

        // A wrong row of InlineData would pass the reason check with the wrong line, so check both.
        Assert.Equal((line, reason), (ex.Line, ex.Reason));
    }

    [Fact]
    public void The_validator_reports_a_bad_segment_count_and_checks_the_next_widget()
    {
        var (_, errors) = Layouts.Collect(Header + """
            - { type: bar, at: [0, 0], size: [400, 40], source: x, segments: 500 }
            - { type: gauge, at: [0, 0], size: [200, 200], source: x, segments: 100 }
            """);

        Assert.Equal(
            [
                "3: segments must be between 2 and 120",
                "4: 100 segments of at least 1° with 3° gaps need 397°, but this gauge sweeps 270°; use fewer segments or a smaller gap",
            ],
            Layouts.Lines(errors));
    }

    // ---- Which segments light ----

    /// With 10 segments over 0 to 100, segment n lights at (n - 0.5) × 10: the lit length is the nearest whole number
    /// of segments.
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(4.999, 0)]
    [InlineData(5, 1)]
    [InlineData(14.999, 1)]
    [InlineData(15, 2)]
    [InlineData(50, 5)]
    [InlineData(94.999, 9)]
    [InlineData(95, 10)]
    [InlineData(100, 10)]
    [InlineData(150, 10)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 10)]
    [InlineData(double.NegativeInfinity, 0)]
    public void A_segment_lights_when_the_value_reaches_its_middle(double value, int lit) =>
        Assert.Equal(lit, SegmentPainter.Lit(value, 0, 100, 10));

    [Theory]
    [InlineData(20, 0)]
    [InlineData(23.999, 0)]
    [InlineData(24, 1)]
    [InlineData(96, 10)]
    public void Lighting_follows_the_range(double value, int lit) =>
        Assert.Equal(lit, SegmentPainter.Lit(value, 20, 100, 10));

    // ---- Rendering ----

    const string Bar = """
          - type: bar
            at: [0, 0]
            size: [400, 40]
            source: gpu.load
            segments: 10
            style: { fill: "#FF0000", track: "#404040" }
        """;

    [Fact]
    public void Horizontal_segments_light_from_the_left_with_gaps_between()
    {
        // Segments 36.4 px long with 4 px gaps, 40.4 px apart: segment 4 is x 161.6 to 198, segment 5 202 to 238.4.
        using var frame = Render(Bar, ("gpu.load", 50));

        Pixels.AssertNear(SKColors.Red, At(frame, 18, 20));
        Pixels.AssertNear(SKColors.Red, At(frame, 180, 20));
        Pixels.AssertNear(SKColors.Black, At(frame, 38, 20), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 200, 20), 0);
        Pixels.AssertNear(Track, At(frame, 220, 20));
        Pixels.AssertNear(Track, At(frame, 390, 20));
    }

    [Fact]
    public void A_value_short_of_a_segments_middle_leaves_it_unlit()
    {
        using var frame = Render(Bar, ("gpu.load", 44.9));

        Pixels.AssertNear(SKColors.Red, At(frame, 140, 20));
        Pixels.AssertNear(Track, At(frame, 180, 20));
    }

    [Fact]
    public void Vertical_segments_light_from_the_bottom()
    {
        // A quarter of the range lights 2.5 + 0.5 = 3 segments: y 364 to 400, 323 to 360 and 283 to 319.
        using var frame = Render("""
              - type: bar
                at: [0, 0]
                size: [40, 400]
                source: cpu.power
                max: 200
                direction: vertical
                segments: 10
                style: { fill: "#FF0000", track: "#404040" }
            """, ("cpu.power", 50));

        Pixels.AssertNear(SKColors.Red, At(frame, 20, 380));
        Pixels.AssertNear(SKColors.Red, At(frame, 20, 340));
        Pixels.AssertNear(SKColors.Black, At(frame, 20, 321), 0);
        Pixels.AssertNear(SKColors.Red, At(frame, 20, 300));
        Pixels.AssertNear(Track, At(frame, 20, 260));
        Pixels.AssertNear(Track, At(frame, 20, 20));
    }

    [Fact]
    public void Stale_segmented_bar_shows_only_its_track()
    {
        using var frame = Render(Bar);

        Pixels.AssertNear(Track, At(frame, 18, 20));
        Pixels.AssertNear(Track, At(frame, 382, 20));
    }

    [Fact]
    public void A_gradient_spans_the_whole_bar_and_each_segment_shows_its_part()
    {
        const string gradient = """
              - type: bar
                at: [0, 0]
                size: [400, 40]
                source: gpu.load
                segments: 10
                style: { fill: { linear: ["#00FF00", "#FF0000"] } }
            """;
        using var full = Render(gradient, ("gpu.load", 100));
        using var half = Render(gradient, ("gpu.load", 50));

        Pixels.AssertNear(SKColors.Lime, At(full, 1, 20), 12);
        Pixels.AssertNear(SKColors.Red, At(full, 398, 20), 12);
        // The last lit segment at half is mid-gradient, as a continuous bar's fill is there, not the gradient's end.
        Pixels.AssertNear(At(full, 196, 20), At(half, 196, 20), 2);
        Assert.True(At(half, 196, 20).Green > 100);
    }

    [Fact]
    public void Radius_rounds_each_segments_corners()
    {
        using var frame = Render("""
              - type: bar
                at: [0, 0]
                size: [400, 40]
                source: gpu.load
                segments: 10
                style: { fill: "#FF0000", radius: 10 }
            """, ("gpu.load", 100));

        Pixels.AssertNear(SKColors.Red, At(frame, 18, 20));
        Pixels.AssertNear(SKColors.Black, At(frame, 0, 0), 0);
        // Segment 1 starts at x 40.4: its corner is rounded too, not only the bar's.
        Pixels.AssertNear(SKColors.Black, At(frame, 41, 1), 0);
        Pixels.AssertNear(SKColors.Red, At(frame, 41, 20));
    }

    const string Ring = """
          - type: gauge
            at: [0, 0]
            size: [200, 200]
            source: gpu.temp
            start: -90
            sweep: 360
            thickness: 20
            segments: 4
            gap: 10
            style: { fill: "#FF0000", track: "#404040" }
        """;

    [Fact]
    public void Ring_segments_light_clockwise_with_a_gap_at_the_join()
    {
        // Four 80° segments: -85 to -5, 5 to 85, 95 to 175 and 185 to 265, so the gaps sit at 12, 3, 6 and 9 o'clock.
        using var frame = Render(Ring, ("gpu.temp", 50));

        Pixels.AssertNear(SKColors.Red, OnRing(frame, -45));
        Pixels.AssertNear(SKColors.Red, OnRing(frame, 45));
        Pixels.AssertNear(Track, OnRing(frame, 135));
        Pixels.AssertNear(Track, OnRing(frame, 225));
        Pixels.AssertNear(SKColors.Black, OnRing(frame, -90), 0);
        Pixels.AssertNear(SKColors.Black, OnRing(frame, 0), 0);
        Pixels.AssertNear(SKColors.Black, OnRing(frame, 90), 0);
    }

    [Fact]
    public void An_anticlockwise_arc_lights_from_its_start()
    {
        using var frame = Render("""
              - type: gauge
                at: [0, 0]
                size: [200, 200]
                source: gpu.temp
                start: 0
                sweep: -180
                thickness: 20
                segments: 2
                gap: 20
                style: { fill: "#FF0000", track: "#404040" }
            """, ("gpu.temp", 50));

        // Segments 80° long: 0 to -80 (lit) and -100 to -180.
        Pixels.AssertNear(SKColors.Red, OnRing(frame, -40));
        Pixels.AssertNear(SKColors.Black, OnRing(frame, -90), 0);
        Pixels.AssertNear(Track, OnRing(frame, -140));
        Pixels.AssertNear(SKColors.Black, OnRing(frame, 90), 0);
    }

    [Fact]
    public void When_rules_recolor_every_segment()
    {
        using var frame = Render(Ring + """

                when:
                  - { above: 80, style: { fill: "#00FF00", track: "#0000FF" } }
            """, ("gpu.temp", 85));

        Pixels.AssertNear(SKColors.Lime, OnRing(frame, -45));
        Pixels.AssertNear(SKColors.Lime, OnRing(frame, 135));
        Pixels.AssertNear(SKColors.Blue, OnRing(frame, 225));
    }

    [Fact]
    public void Smoothed_segments_light_as_the_eased_value_crosses_them()
    {
        using var folder = new TempTheme(Header + Bar.Replace("segments: 10", "segments: 10\n    smooth: 1000"));
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme);

        using (renderer.Render(TestSensors.Of(("gpu.load", 0)), TimeSpan.Zero)) { }
        using (renderer.Render(TestSensors.Of(("gpu.load", 100)), TimeSpan.FromSeconds(1))) { }
        using var halfway = renderer.Render(TestSensors.Of(("gpu.load", 100)), TimeSpan.FromSeconds(1.5));

        // Cubic ease-out is 87.5% of the way at half the duration: 8.75 segments rounds to 9 lit.
        Pixels.AssertNear(SKColors.Red, At(halfway, 340, 20));
        Pixels.AssertNear(Track, At(halfway, 382, 20));
    }

    [Fact]
    public void Segments_snapshot()
    {
        using var frame = Frames.Render(Landscape + """
            styles:
              led: { fill: "#36D1DC", track: "#FFFFFF18" }
              meter: { fill: { linear: ["#3DDC84", "#F6D743", "#FF5A36"] }, track: "#FFFFFF14" }
              rising: { fill: { linear: ["#3DDC84", "#F6D743", "#FF5A36"], angle: 270 }, track: "#FFFFFF14" }
            background:
              - fill: "#0B0F1A"
            widgets:
              - { type: bar, at: [40, 40], size: [560, 40], source: cpu.load, segments: 12, style: led }
              - { type: bar, at: [40, 100], size: [560, 40], source: gpu.load, segments: 12, style: led }
              - { type: bar, at: [40, 160], size: [560, 40], source: ram.used_pct, segments: 12, style: led }
              - { type: bar, at: [40, 240], size: [560, 56], source: gpu.temp, segments: 24, gap: 3, style: meter }
              - type: bar
                at: [40, 336]
                size: [560, 56]
                source: cpu.temp
                segments: 16
                gap: 6
                style: { fill: "#A770EF", track: "#FFFFFF18", radius: 8 }
                when:
                  - { above: 85, style: { fill: "#FF5A36" } }
              - { type: bar, at: [660, 40], size: [48, 400], source: cpu.load, direction: vertical, segments: 20, gap: 4, style: rising }
              - { type: bar, at: [740, 40], size: [48, 400], source: gpu.load, direction: vertical, segments: 20, gap: 4, style: rising }
              - { type: bar, at: [820, 40], size: [48, 400], source: ram.used_pct, direction: vertical, segments: 10, gap: 8, style: { fill: "#36D1DC", track: "#FFFFFF18", radius: 6 } }
              - type: gauge
                at: [920, 40]
                size: [400, 400]
                source: gpu.temp
                min: 20
                max: 100
                thickness: 40
                segments: 30
                gap: 3
                style: { fill: { linear: ["#36D1DC", "#FF5A36"] }, track: "#FFFFFF18" }
              - { type: value, at: [1020, 190], size: [200, 100], source: gpu.temp, format: "{0:0}°", style: { size: 72, weight: bold, align: center } }
              - type: gauge
                at: [1380, 40]
                size: [400, 400]
                source: cpu.load
                start: -90
                sweep: 360
                thickness: 48
                segments: 12
                gap: 6
                style: { fill: "#A770EF", track: "#FFFFFF18", radius: 10 }
              - { type: gauge, at: [1490, 150], size: [180, 180], source: ram.used_pct, start: -90, sweep: -360, thickness: 20, segments: 40, gap: 2, style: led }
            """, TestSensors.Of(("cpu.load", 37), ("gpu.load", 66), ("ram.used_pct", 100), ("gpu.temp", 74), ("cpu.temp", 91)));

        Snapshots.Match(frame, "segments");
    }
}
