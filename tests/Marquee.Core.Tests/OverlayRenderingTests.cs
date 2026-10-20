using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Rotation, blend modes and hidden widgets and layers, as drawn.
public class OverlayRenderingTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    /// The common background of the blend tests, and the color blended onto it.
    const string Below = "#4060A0";
    const string Above = "#C08040";

    static SKImage Render(string layout, params (string, object)[] values) =>
        Frames.Render(Landscape + layout, TestSensors.Of(values));

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    [Fact]
    public void A_rotated_widget_turns_clockwise_about_its_own_centre()
    {
        using var frame = Render("""
            widgets:
              - { type: text, at: [100, 100], size: [200, 20], text: " ", rotate: 90, style: { fill: "#FF0000" } }
              - { type: text, at: [400, 100], size: [100, 100], text: " ", rotate: 45, style: { fill: "#00FF00" } }
            """);

        // The bar's centre (200, 110) stays put, and it now runs from y 10 to 210.
        Pixels.AssertNear(SKColors.Red, At(frame, 200, 20));
        Pixels.AssertNear(SKColors.Red, At(frame, 200, 200));
        Pixels.AssertNear(SKColors.Black, At(frame, 120, 110), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 280, 110), 0);

        // The square's corner now points straight up from its centre (450, 150), above its old top edge.
        Pixels.AssertNear(SKColors.Lime, At(frame, 450, 85));
        Pixels.AssertNear(SKColors.Black, At(frame, 405, 105), 0);
    }

    [Fact]
    public void A_rotated_group_turns_its_children_with_it()
    {
        using var frame = Render("""
            widgets:
              - type: group
                at: [100, 100]
                size: [200, 100]
                rotate: 180
                children:
                  - { type: text, at: [0, 0], size: [20, 20], text: " ", style: { fill: "#0000FF" } }
            """);

        // The child's top-left corner turns about the group's centre (200, 150) to the bottom right.
        Pixels.AssertNear(SKColors.Blue, At(frame, 290, 190));
        Pixels.AssertNear(SKColors.Black, At(frame, 110, 110), 0);
    }

    /// Each mode's result for Above over Below, both opaque: (192, 128, 64) onto (64, 96, 160).
    [Theory]
    [InlineData("normal", 192, 128, 64)]
    [InlineData("multiply", 48, 48, 40)]
    [InlineData("screen", 208, 176, 184)]
    [InlineData("overlay", 96, 96, 113)]
    [InlineData("darken", 64, 96, 64)]
    [InlineData("lighten", 192, 128, 160)]
    [InlineData("add", 255, 224, 224)]
    public void A_blended_widget_mixes_with_what_is_under_it(string blend, int r, int g, int b)
    {
        using var frame = Render($$"""
            background:
              - fill: "{{Below}}"
            widgets:
              - { type: text, at: [0, 0], size: [100, 100], text: " ", style: { fill: "{{Above}}", blend: {{blend}} } }
            """);

        Pixels.AssertNear(new SKColor((byte)r, (byte)g, (byte)b), At(frame, 50, 50), 3);
        Pixels.AssertNear(new SKColor(0x40, 0x60, 0xA0), At(frame, 150, 50), 0);
    }

    [Fact]
    public void Opacity_applies_to_a_blended_widget()
    {
        using var frame = Render($$"""
            background:
              - fill: "{{Below}}"
            widgets:
              - { type: text, at: [0, 0], size: [100, 100], text: " ", style: { fill: "{{Above}}", blend: multiply, opacity: 0.5 } }
            """);

        // Halfway between the background and the multiplied color.
        Pixels.AssertNear(new SKColor(56, 72, 100), At(frame, 50, 50), 3);
    }

    [Fact]
    public void A_when_rule_can_change_the_blend()
    {
        var layout = $$"""
            background:
              - fill: "{{Below}}"
            widgets:
              - type: text
                at: [0, 0]
                size: [100, 100]
                text: " "
                source: x
                style: { fill: "{{Above}}" }
                when:
                  - { above: 50, style: { blend: multiply } }
            """;

        using var low = Render(layout, ("x", 20));
        using var high = Render(layout, ("x", 80));

        Pixels.AssertNear(new SKColor(192, 128, 64), At(low, 50, 50), 3);
        Pixels.AssertNear(new SKColor(48, 48, 40), At(high, 50, 50), 3);
    }

    [Fact]
    public void Hidden_widgets_groups_and_layers_leave_no_trace()
    {
        const string background = """
            background:
              - fill: { linear: ["#101828", "#283850"] }

            """;
        const string widgets = """
              - { type: text, at: [40, 40], size: [400, 80], text: SHOWN, style: { size: 48, fill: "#FFFFFF20" } }
              - type: group
                at: [500, 40]
                size: [400, 200]
                style: { stroke: { width: 2, color: "#36D1DC" } }
                children:
                  - { type: bar, at: [20, 20], size: [360, 30], source: cpu.load, style: { fill: "#36D1DC" } }

            """;
        using var plain = Render(background + "widgets:\n" + widgets, ("cpu.load", 40));
        using var hidden = Render("""
            background:
              - fill: { linear: ["#101828", "#283850"] }
              - { fill: "#FF0000", hidden: true }
              - { fill: "#00FF00", blend: add, hidden: true, locked: true }
            widgets:
              - { type: text, at: [40, 40], size: [400, 80], text: SHOWN, locked: true, style: { size: 48, fill: "#FFFFFF20" } }
              - { type: text, at: [0, 0], size: [1920, 480], text: HIDDEN, hidden: true, style: { size: 200, fill: "#FF00FF" } }
              - type: group
                at: [500, 40]
                size: [400, 200]
                style: { stroke: { width: 2, color: "#36D1DC" } }
                children:
                  - { type: bar, at: [20, 20], size: [360, 30], source: cpu.load, style: { fill: "#36D1DC" } }
                  - { type: gauge, at: [20, 60], size: [120, 120], source: cpu.load, hidden: true, style: { fill: "#FF0000" } }
              - type: group
                at: [1000, 40]
                size: [600, 400]
                hidden: true
                style: { fill: "#FFFF00" }
                children:
                  - { type: text, at: [0, 0], size: [600, 400], text: CHILD, style: { size: 120 } }
            """, ("cpu.load", 40));

        using var a = SKBitmap.FromImage(plain);
        using var b = SKBitmap.FromImage(hidden);
        Assert.True(a.Bytes.AsSpan().SequenceEqual(b.Bytes), "The frame with hidden widgets and layers differs from the frame without them.");
    }

    [Fact]
    public void Rotation_snapshot()
    {
        using var frame = Frames.Render(Landscape + """
            styles:
              label: { size: 28, weight: semibold, color: "#E6ECF5", align: center }
            background:
              - fill: "#0B0F1A"
            widgets:
              - { type: text, at: [60, 190], size: [360, 100], text: TILTED, rotate: -15, style: { size: 56, weight: bold, align: center, fill: "#FF5A36", radius: 16 } }
              - type: gauge
                at: [480, 60]
                size: [360, 360]
                source: gpu.temp
                min: 20
                max: 100
                thickness: 30
                rotate: 90
                style: { fill: { linear: ["#36D1DC", "#FF5A36"] }, track: "#FFFFFF18", stroke: { cap: round } }
              - { type: bar, at: [900, 220], size: [320, 40], source: cpu.load, rotate: 45, style: { fill: "#A770EF", track: "#FFFFFF18", radius: 20 } }
              - type: group
                at: [1320, 90]
                size: [480, 300]
                rotate: 20
                style: { fill: "#FFFFFF10", radius: 24, stroke: { width: 3, color: "#36D1DC" } }
                children:
                  - { type: text, at: [0, 30], size: [480, 50], text: GROUP, style: label }
                  - { type: bar, at: [40, 120], size: [400, 36], source: cpu.load, style: { fill: "#36D1DC", track: "#FFFFFF18", radius: 18 } }
                  - { type: text, at: [40, 190], size: [400, 70], text: CHILD, rotate: -20, style: { size: 40, align: center, fill: "#FF5A3680", radius: 12 } }
            """, TestSensors.Of(("gpu.temp", 68), ("cpu.load", 62)));

        Snapshots.Match(frame, "overlays-rotate");
    }

    [Fact]
    public void Blend_modes_snapshot()
    {
        string[] modes = ["normal", "multiply", "screen", "overlay", "darken", "lighten", "add"];
        // Each tile's own text blends with it; the plain label under it stays readable.
        var tiles = string.Concat(modes.Select((mode, i) => $$"""
              - type: text
                at: [{{48 + i * 264}}, 60]
                size: [240, 280]
                text: {{mode}}
                style: { size: 36, weight: bold, align: center, radius: 24, fill: { linear: ["#36D1DC", "#FFB347"], angle: 90 }, blend: {{mode}} }
              - { type: text, at: [{{48 + i * 264}}, 360], size: [240, 60], text: {{mode}}, style: { size: 28, align: center } }

            """));

        using var frame = Frames.Render(Landscape + """
            background:
              - fill: { linear: ["#1E3C72", "#B0306A", "#F2C94C"] }
              - fill: { radial: ["#FFFFFF40", "#00000000"] }
            widgets:

            """ + tiles, TestSensors.Of());

        Snapshots.Match(frame, "overlays-blend");
    }
}
