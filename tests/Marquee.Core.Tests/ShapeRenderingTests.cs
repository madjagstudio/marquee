using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// The shape widget: `type: shape` with a rect, ellipse, line or arc.
public class ShapeRenderingTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    static SKImage Render(string widgets, params (string, object)[] values) =>
        Frames.Render(Landscape + "widgets:\n" + widgets, TestSensors.Of(values));

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    [Fact]
    public void Rect_draws_as_a_box_does_with_radius_corners_and_the_stroke_inside()
    {
        using var frame = Render("""
              - type: shape
                shape: rect
                at: [100, 100]
                size: [200, 100]
                style: { fill: "#FF0000", radius: 30, stroke: { width: 10, color: "#0000FF" } }
            """);

        Pixels.AssertNear(SKColors.Red, At(frame, 200, 150));
        Pixels.AssertNear(SKColors.Blue, At(frame, 104, 150));
        Pixels.AssertNear(SKColors.Black, At(frame, 96, 150), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 102, 102), 0);
    }

    [Fact]
    public void Ellipse_fills_its_box()
    {
        using var frame = Render("""
              - { type: shape, shape: ellipse, at: [0, 0], size: [200, 100], style: { fill: "#FF0000" } }
            """);

        Pixels.AssertNear(SKColors.Red, At(frame, 100, 50));
        Pixels.AssertNear(SKColors.Red, At(frame, 100, 3));
        Pixels.AssertNear(SKColors.Red, At(frame, 197, 50));
        Pixels.AssertNear(SKColors.Black, At(frame, 8, 8), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 192, 92), 0);
    }

    [Fact]
    public void Ellipse_stroke_is_drawn_inside_its_box()
    {
        using var frame = Render("""
              - { type: shape, shape: ellipse, at: [0, 0], size: [200, 100], style: { stroke: { width: 10, color: "#0000FF" } } }
            """);

        Pixels.AssertNear(SKColors.Blue, At(frame, 100, 5));
        Pixels.AssertNear(SKColors.Blue, At(frame, 5, 50));
        Pixels.AssertNear(SKColors.Black, At(frame, 100, 50), 0);
    }

    [Fact]
    public void Line_with_no_height_is_horizontal_and_its_stroke_is_centred_on_it()
    {
        using var frame = Render("""
              - { type: shape, shape: line, at: [100, 50], size: [200, 0], style: { stroke: { width: 10, color: "#FF0000" } } }
            """);

        Pixels.AssertNear(SKColors.Red, At(frame, 200, 46));
        Pixels.AssertNear(SKColors.Red, At(frame, 200, 53));
        Pixels.AssertNear(SKColors.Black, At(frame, 200, 40), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 200, 60), 0);
        // A butt cap ends at the line's end.
        Pixels.AssertNear(SKColors.Black, At(frame, 97, 50), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 303, 50), 0);
    }

    [Fact]
    public void Line_with_no_width_is_vertical_and_takes_the_stroke_cap()
    {
        using var frame = Render("""
              - { type: shape, shape: line, at: [50, 100], size: [0, 200], style: { stroke: { width: 10, color: "#FF0000", cap: round } } }
            """);

        Pixels.AssertNear(SKColors.Red, At(frame, 50, 200));
        Pixels.AssertNear(SKColors.Black, At(frame, 40, 200), 0);
        // A round cap reaches past each end by half the width.
        Pixels.AssertNear(SKColors.Red, At(frame, 50, 97));
        Pixels.AssertNear(SKColors.Red, At(frame, 50, 303));
        Pixels.AssertNear(SKColors.Black, At(frame, 50, 90), 0);
    }

    [Fact]
    public void Line_runs_from_the_top_left_corner_to_the_bottom_right()
    {
        using var frame = Render("""
              - { type: shape, shape: line, at: [0, 0], size: [100, 100], style: { stroke: { width: 6, color: "#FF0000" } } }
            """);

        Pixels.AssertNear(SKColors.Red, At(frame, 10, 10));
        Pixels.AssertNear(SKColors.Red, At(frame, 50, 50));
        Pixels.AssertNear(SKColors.Red, At(frame, 90, 90));
        Pixels.AssertNear(SKColors.Black, At(frame, 90, 10), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 10, 90), 0);
    }

    [Fact]
    public void Arc_draws_like_a_gauge_with_no_value()
    {
        using var frame = Render("""
              - type: shape
                shape: arc
                at: [0, 0]
                size: [300, 200]
                start: -90
                sweep: 90
                thickness: 20
                style: { fill: "#FF0000" }
            """);

        // In the largest square that fits, centred: centre (150, 100), the band's middle at radius 90.
        Pixels.AssertNear(SKColors.Red, At(frame, 214, 36));
        Pixels.AssertNear(SKColors.Red, At(frame, 166, 11));
        Pixels.AssertNear(SKColors.Red, At(frame, 238, 84));
        Pixels.AssertNear(SKColors.Black, At(frame, 150, 185), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 65, 100), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 150, 100), 0);
    }

    [Fact]
    public void Arc_stroke_outlines_its_band_inside_it()
    {
        using var frame = Render("""
              - type: shape
                shape: arc
                at: [0, 0]
                size: [200, 200]
                start: -90
                sweep: 90
                thickness: 40
                style: { fill: "#FF0000", stroke: { width: 6, color: "#0000FF" } }
            """);

        // The band runs from radius 60 to 100 about (100, 100); along 45° its middle is red and its edges blue.
        Pixels.AssertNear(SKColors.Red, At(frame, 157, 43));
        Pixels.AssertNear(SKColors.Blue, At(frame, 168, 32));
        Pixels.AssertNear(SKColors.Blue, At(frame, 145, 55));
        Pixels.AssertNear(SKColors.Black, At(frame, 174, 26), 0);
    }

    [Fact]
    public void Shape_is_recolored_by_a_when_rule()
    {
        const string shape = """
              - type: shape
                shape: rect
                at: [0, 0]
                size: [100, 100]
                source: gpu.temp
                style: { fill: "#00FF00" }
                when:
                  - { above: 80, style: { fill: "#FF0000" } }
                  - { stale: true, style: { fill: "#808080" } }
            """;

        using var cool = Render(shape, ("gpu.temp", 60));
        using var hot = Render(shape, ("gpu.temp", 90));
        using var stale = Render(shape);

        Pixels.AssertNear(SKColors.Lime, At(cool, 50, 50));
        Pixels.AssertNear(SKColors.Red, At(hot, 50, 50));
        Pixels.AssertNear(new SKColor(0x80, 0x80, 0x80), At(stale, 50, 50));
    }

    [Fact]
    public void Line_stroked_only_by_a_when_rule_shows_only_while_it_holds()
    {
        const string line = """
              - type: shape
                shape: line
                at: [0, 50]
                size: [200, 0]
                source: gpu.temp
                style: { stroke: { width: 10 } }
                when:
                  - { above: 80, style: { stroke: { color: "#FF0000" } } }
            """;

        using var cool = Render(line, ("gpu.temp", 60));
        using var hot = Render(line, ("gpu.temp", 90));

        Pixels.AssertNear(SKColors.Black, At(cool, 100, 50), 0);
        Pixels.AssertNear(SKColors.Red, At(hot, 100, 50));
    }

    [Fact]
    public void Glow_spreads_around_the_shape()
    {
        using var frame = Render("""
              - { type: shape, shape: rect, at: [100, 100], size: [100, 100], style: { fill: "#FF0000", glow: { color: "#00FF00", radius: 24 } } }
            """);

        var outside = At(frame, 206, 150);
        Assert.True(outside.Green > 40 && outside.Red < 10 && outside.Blue < 10, $"Expected a green glow, got {outside}");
        Pixels.AssertNear(SKColors.Red, At(frame, 150, 150));
        Pixels.AssertNear(SKColors.Black, At(frame, 300, 150), 0);
    }

    [Fact]
    public void Shadow_is_offset_under_the_shape()
    {
        using var frame = Render("""
              - { type: shape, shape: ellipse, at: [100, 100], size: [100, 100], style: { fill: "#FF0000", shadow: { color: "#0000FF", x: 20, y: 20 } } }
            """);

        Pixels.AssertNear(SKColors.Red, At(frame, 150, 150));
        Pixels.AssertNear(SKColors.Blue, At(frame, 210, 170));
        Pixels.AssertNear(SKColors.Black, At(frame, 90, 150), 0);
    }

    [Fact]
    public void Shape_takes_its_style_opacity()
    {
        using var frame = Render("""
              - { type: shape, shape: rect, at: [0, 0], size: [100, 100], style: { fill: "#FFFFFF", opacity: 0.5 } }
            """);

        Pixels.AssertNear(new SKColor(128, 128, 128), At(frame, 50, 50), 2);
    }

    [Fact]
    public void Shape_widgets_snapshot()
    {
        var store = TestSensors.Store(("cpu.temp", 91), ("gpu.temp", 64));

        using var frame = Frames.Render(Landscape + """
            background:
              - fill: "#0B0F1A"
            widgets:
              - type: shape
                shape: rect
                at: [40, 40]
                size: [320, 160]
                style: { fill: { linear: ["#36D1DC", "#5B86E5"] }, radius: 24, stroke: { width: 4, color: "#FFFFFF" }, shadow: { color: "#000000C0", x: 8, y: 8, blur: 12 } }
              - type: shape
                shape: rect
                at: [40, 260]
                size: [320, 160]
                style: { fill: "#FFFFFF10", stroke: { width: 2, color: "#FF5A36" }, glow: { color: "#FF5A36", radius: 20 } }
              - type: shape
                shape: ellipse
                at: [420, 40]
                size: [320, 160]
                style: { fill: { radial: ["#A770EF", "#1E2A44"] }, glow: { color: "#A770EF", radius: 16 } }
              - type: shape
                shape: ellipse
                at: [460, 260]
                size: [160, 160]
                style: { stroke: { width: 8, color: "#36D1DC" }, opacity: 0.6 }
              - { type: shape, shape: line, at: [800, 60], size: [320, 0], style: { stroke: { width: 6, color: "#FFFFFF", cap: round } } }
              - { type: shape, shape: line, at: [800, 120], size: [320, 0], style: { stroke: { width: 6, color: { linear: ["#36D1DC", "#FF5A36"] } } } }
              - { type: shape, shape: line, at: [1160, 40], size: [0, 400], style: { stroke: { width: 4, color: "#FFFFFF60" } } }
              - type: shape
                shape: line
                at: [800, 180]
                size: [320, 240]
                style: { stroke: { width: 8, color: "#A770EF", cap: square }, glow: { color: "#A770EF", radius: 20 }, shadow: { color: "#000000", x: 6, y: 6 } }
              - type: shape
                shape: arc
                at: [1200, 40]
                size: [300, 300]
                thickness: 36
                style: { fill: { linear: ["#36D1DC", "#FF5A36"] }, stroke: { cap: round } }
              - type: shape
                shape: arc
                at: [1540, 40]
                size: [340, 300]
                start: -90
                sweep: 240
                thickness: 48
                source: cpu.temp
                style: { fill: "#36D1DC", stroke: { width: 4, color: "#FFFFFF", cap: round }, glow: { color: "#36D1DC", radius: 16 } }
                when:
                  - { above: 85, style: { fill: "#FF5A36", glow: { color: "#FF5A36" } } }
              - type: shape
                shape: rect
                at: [1240, 380]
                size: [600, 60]
                source: gpu.temp
                style: { fill: "#36D1DC", radius: 30 }
                when:
                  - { above: 85, style: { fill: "#FF5A36" } }
            """, store.Snapshot());

        Snapshots.Match(frame, "shape-widgets");
    }
}
