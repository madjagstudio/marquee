using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class ShapeWidgetTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";
    static readonly SKColor Track = new(0x40, 0x40, 0x40);

    static SKImage Render(string widgets, params (string, object)[] values) =>
        Frames.Render(Landscape + "widgets:\n" + widgets, TestSensors.Of(values));

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    const string Bar = """
          - type: bar
            at: [0, 0]
            size: [400, 40]
            source: gpu.load
            style: { fill: "#FF0000", track: "#404040" }
        """;

    [Fact]
    public void Horizontal_bar_fills_its_fraction_from_the_left()
    {
        using var frame = Render(Bar, ("gpu.load", 50));

        Pixels.AssertNear(SKColors.Red, At(frame, 100, 20));
        Pixels.AssertNear(SKColors.Red, At(frame, 195, 20));
        Pixels.AssertNear(Track, At(frame, 205, 20));
        Pixels.AssertNear(Track, At(frame, 390, 20));
    }

    [Fact]
    public void Vertical_bar_fills_from_the_bottom()
    {
        using var frame = Render("""
              - type: bar
                at: [0, 0]
                size: [40, 400]
                source: cpu.power
                min: 0
                max: 200
                direction: vertical
                style: { fill: "#FF0000", track: "#404040" }
            """, ("cpu.power", 50));

        Pixels.AssertNear(SKColors.Red, At(frame, 20, 390));
        Pixels.AssertNear(Track, At(frame, 20, 290));
    }

    [Theory]
    [InlineData(150, true)]
    [InlineData(-10, false)]
    public void Bar_clamps_to_its_range(double load, bool full)
    {
        using var frame = Render(Bar, ("gpu.load", load));

        Pixels.AssertNear(full ? SKColors.Red : Track, At(frame, 2, 20));
        Pixels.AssertNear(full ? SKColors.Red : Track, At(frame, 398, 20));
    }

    [Fact]
    public void Stale_bar_shows_only_its_track()
    {
        using var frame = Render(Bar);

        Pixels.AssertNear(Track, At(frame, 2, 20));
    }

    [Fact]
    public void Bar_gradient_spans_the_whole_bar()
    {
        using var frame = Render("""
              - type: bar
                at: [0, 0]
                size: [400, 40]
                source: gpu.load
                style: { fill: { linear: ["#00FF00", "#FF0000"] } }
            """, ("gpu.load", 100));

        Pixels.AssertNear(SKColors.Lime, At(frame, 1, 20), 12);
        Pixels.AssertNear(SKColors.Red, At(frame, 398, 20), 12);
    }

    [Fact]
    public void Smoothed_bar_eases_to_a_new_value_across_frames()
    {
        using var folder = new TempTheme(Landscape + "widgets:\n" + Bar.Replace("source: gpu.load", "source: gpu.load\n    smooth: 1000"));
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme);

        using (renderer.Render(TestSensors.Of(("gpu.load", 0)), TimeSpan.Zero)) { }
        using (renderer.Render(TestSensors.Of(("gpu.load", 100)), TimeSpan.FromSeconds(1))) { }
        using var halfway = renderer.Render(TestSensors.Of(("gpu.load", 100)), TimeSpan.FromSeconds(1.5));

        // Cubic ease-out is 87.5% of the way at half the duration: filled to x = 350.
        Pixels.AssertNear(SKColors.Red, At(halfway, 340, 20));
        Pixels.AssertNear(Track, At(halfway, 360, 20));
    }

    [Fact]
    public void Gauge_sweeps_clockwise_from_its_start_angle()
    {
        using var frame = Render("""
              - type: gauge
                at: [0, 0]
                size: [200, 200]
                source: gpu.temp
                start: -90
                sweep: 360
                thickness: 20
                style: { fill: "#FF0000", track: "#404040" }
            """, ("gpu.temp", 25));

        // Centre (100, 100), arc radius 90: 12 o'clock to 3 o'clock is filled.
        Pixels.AssertNear(SKColors.Red, At(frame, 164, 36));
        Pixels.AssertNear(Track, At(frame, 100, 190));
        Pixels.AssertNear(Track, At(frame, 10, 100));
        Pixels.AssertNear(SKColors.Black, At(frame, 100, 100), 0);
    }

    [Fact]
    public void Default_gauge_arc_opens_at_the_bottom()
    {
        using var frame = Render("""
              - type: gauge
                at: [0, 0]
                size: [200, 200]
                source: gpu.temp
                thickness: 20
                style: { fill: "#FF0000", track: "#404040" }
            """, ("gpu.temp", 100));

        Pixels.AssertNear(SKColors.Red, At(frame, 10, 100));
        Pixels.AssertNear(SKColors.Red, At(frame, 190, 100));
        Pixels.AssertNear(SKColors.Black, At(frame, 100, 190), 0);
    }

    [Fact]
    public void Graph_draws_history_with_the_newest_sample_on_the_right()
    {
        var store = TestSensors.Store();
        store.Set("gpu.load", 0);
        store.Set("gpu.load", 100);
        using var frame = Frames.Render(Landscape + """
            widgets:
              - type: graph
                at: [0, 0]
                size: [200, 100]
                source: gpu.load
                points: 2
                style: { stroke: { width: 4, color: "#FF0000" }, fill: "#0000FF" }
            """, store.Snapshot());

        Pixels.AssertNear(SKColors.Red, At(frame, 100, 50), 40);
        Pixels.AssertNear(SKColors.Blue, At(frame, 180, 90));
        Pixels.AssertNear(SKColors.Black, At(frame, 20, 10), 0);
    }

    [Fact]
    public void Graph_with_one_fixed_bound_below_the_data_pins_the_line_to_that_edge()
    {
        var store = TestSensors.Store();
        store.Set("gpu.load", 55);
        store.Set("gpu.load", 60);
        using var frame = Frames.Render(Landscape + """
            widgets:
              - type: graph
                at: [0, 0]
                size: [200, 100]
                source: gpu.load
                max: 50
                points: 2
                style: { stroke: { width: 4, color: "#FF0000" } }
            """, store.Snapshot());

        Pixels.AssertNear(SKColors.Red, At(frame, 100, 1), 60);
    }

    [Fact]
    public void Graph_with_fewer_than_two_samples_draws_only_its_track()
    {
        using var frame = Render("""
              - type: graph
                at: [0, 0]
                size: [200, 100]
                source: gpu.load
                style: { track: "#404040" }
            """, ("gpu.load", 50));

        Assert.Equal(0, Pixels.Count(frame, SKRectI.Create(0, 0, 200, 100), Pixels.IsBright));
        Pixels.AssertNear(Track, At(frame, 100, 50));
    }

    [Fact]
    public void Image_widget_fits_its_picture()
    {
        using var folder = new TempTheme(Landscape + """
            widgets:
              - { type: image, at: [0, 0], size: [200, 100], src: red.png }
            """);
        folder.AddImage("red.png", SKColors.Red);
        using var theme = folder.Load();
        using var frame = Frames.Render(theme, TestSensors.Of());

        Pixels.AssertNear(SKColors.Red, At(frame, 100, 50));
        Pixels.AssertNear(SKColors.Black, At(frame, 20, 50), 0);
    }

    [Fact]
    public void Group_positions_and_clips_its_children()
    {
        using var frame = Render("""
              - type: group
                at: [100, 100]
                size: [100, 100]
                children:
                  - { type: text, at: [-50, -50], size: [400, 400], text: " ", style: { fill: "#FF0000" } }
                  - { type: text, at: [10, 10], size: [20, 20], text: " ", style: { fill: "#0000FF" } }
            """);

        Pixels.AssertNear(SKColors.Black, At(frame, 90, 90), 0);
        Pixels.AssertNear(SKColors.Red, At(frame, 150, 150));
        Pixels.AssertNear(SKColors.Black, At(frame, 210, 210), 0);
        Pixels.AssertNear(SKColors.Blue, At(frame, 120, 120));
    }

    [Fact]
    public void Group_text_properties_cascade_including_from_its_when_rules()
    {
        const string group = """
              - type: group
                at: [0, 0]
                size: [400, 120]
                source: gpu.temp
                style: { color: "#00FF00", size: 80 }
                when:
                  - { above: 80, style: { color: "#FF0000" } }
                children:
                  - { type: text, at: [0, 0], size: [400, 120], text: "HOT" }
            """;

        using var cool = Render(group, ("gpu.temp", 60));
        using var hot = Render(group, ("gpu.temp", 90));

        var box = SKRectI.Create(0, 0, 400, 120);
        Assert.True(Pixels.Count(cool, box, c => Pixels.Near(c, SKColors.Lime, 30)) > 300);
        Assert.True(Pixels.Count(hot, box, c => Pixels.Near(c, SKColors.Red, 30)) > 300);
        Assert.Equal(0, Pixels.Count(hot, box, c => Pixels.Near(c, SKColors.Lime, 30)));
    }

    [Fact]
    public void Shapes_snapshot()
    {
        var store = TestSensors.Store(("cpu.load", 37), ("gpu.temp", 68), ("ram.used_pct", 81), ("cpu.temp", 91));
        foreach (var load in new double[] { 20, 35, 30, 55, 80, 72, 64, 90, 85, 60, 45, 50, 70, 95, 88, 66 })
            store.Set("gpu.load", load);

        using var frame = Frames.Render(Landscape + """
            styles:
              label: { size: 24, color: "#9AA4B8", align: center }
            background:
              - fill: "#0B0F1A"
            widgets:
              - type: gauge
                at: [40, 40]
                size: [400, 400]
                source: gpu.temp
                min: 20
                max: 100
                thickness: 36
                style: { fill: { linear: ["#36D1DC", "#FF5A36"] }, track: "#FFFFFF18", stroke: { cap: round } }
              - { type: value, at: [140, 190], size: [200, 100], source: gpu.temp, format: "{0:0}°", style: { size: 72, weight: bold, align: center } }
              - type: group
                at: [520, 60]
                size: [600, 360]
                style: { fill: "#FFFFFF0A", radius: 20, stroke: { width: 2, color: "#FFFFFF30" } }
                children:
                  - { type: text, at: [0, 20], size: [600, 40], text: GPU LOAD, style: label }
                  - type: graph
                    at: [30, 80]
                    size: [540, 240]
                    source: gpu.load
                    min: 0
                    max: 100
                    points: 16
                    style: { stroke: { width: 4, color: "#36D1DC" }, fill: { linear: ["#36D1DC60", "#36D1DC00"], angle: 90 } }
              - type: bar
                at: [1200, 120]
                size: [640, 48]
                source: cpu.load
                style: { fill: { linear: ["#36D1DC", "#5B86E5"] }, track: "#FFFFFF18", radius: 24 }
              - type: bar
                at: [1200, 220]
                size: [640, 48]
                source: ram.used_pct
                style: { fill: "#A770EF", track: "#FFFFFF18", radius: 24 }
              - type: bar
                at: [1200, 320]
                size: [640, 48]
                source: cpu.temp
                style: { fill: "#36D1DC", track: "#FFFFFF18", radius: 24 }
                when:
                  - { above: 85, style: { fill: "#FF5A36" } }
            """, store.Snapshot());

        Snapshots.Match(frame, "shapes");
    }
}
