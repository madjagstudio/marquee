using Marquee.Core.Sensors;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class RendererTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    static SKImage Render(string layout, params (string, object)[] values) => Frames.Render(layout, TestSensors.Of(values));

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    [Fact]
    public void Empty_theme_is_black_at_the_design_size()
    {
        using var landscape = Render(Landscape);
        using var portrait = Render("canvas: { orientation: portrait }");

        Assert.Equal((1920, 480), (landscape.Width, landscape.Height));
        Assert.Equal((480, 1920), (portrait.Width, portrait.Height));
        Pixels.AssertNear(SKColors.Black, At(landscape, 960, 240), 0);
    }

    [Fact]
    public void Fill_layer_paints_the_whole_canvas()
    {
        using var frame = Render(Landscape + "background:\n  - fill: \"#203040\"\n");

        Pixels.AssertNear(new SKColor(0x20, 0x30, 0x40), At(frame, 0, 0), 0);
        Pixels.AssertNear(new SKColor(0x20, 0x30, 0x40), At(frame, 1919, 479), 0);
    }

    [Fact]
    public void Linear_gradients_follow_their_angle()
    {
        using var across = Render(Landscape + "background:\n  - fill: { linear: [\"#FF0000\", \"#0000FF\"] }\n");
        using var down = Render(Landscape + "background:\n  - fill: { linear: [\"#FF0000\", \"#0000FF\"], angle: 90 }\n");

        Pixels.AssertNear(SKColors.Red, At(across, 1, 240));
        Pixels.AssertNear(SKColors.Blue, At(across, 1918, 240));
        Pixels.AssertNear(SKColors.Red, At(down, 960, 1));
        Pixels.AssertNear(SKColors.Blue, At(down, 960, 478));
    }

    [Fact]
    public void Layers_composite_bottom_up_with_opacity()
    {
        using var frame = Render(Landscape + """
            background:
              - fill: "#FF0000"
              - fill: "#0000FF"
                opacity: 0.5
            """);

        Pixels.AssertNear(new SKColor(128, 0, 127), At(frame, 960, 240), 2);
    }

    [Fact]
    public void Image_layers_cover_by_default_and_can_contain()
    {
        using var folder = new TempTheme(Landscape + "background:\n  - image: red.png\n");
        folder.AddImage("red.png", SKColors.Red);
        using (var theme = folder.Load())
        using (var cover = Frames.Render(theme, TestSensors.Of()))
        {
            Pixels.AssertNear(SKColors.Red, At(cover, 0, 0));
            Pixels.AssertNear(SKColors.Red, At(cover, 1919, 479));
        }

        folder.Write(Landscape + "background:\n  - image: red.png\n    fit: contain\n");
        using (var theme = folder.Load())
        using (var contain = Frames.Render(theme, TestSensors.Of()))
        {
            Pixels.AssertNear(SKColors.Black, At(contain, 100, 240));
            Pixels.AssertNear(SKColors.Red, At(contain, 960, 240));
        }
    }

    [Fact]
    public void Value_widget_draws_its_box_and_text()
    {
        using var frame = Render(Landscape + """
            widgets:
              - type: value
                at: [100, 100]
                size: [300, 100]
                source: cpu.temp
                format: "{0:0}°C"
                style: { fill: "#0000FF", size: 60 }
            """, ("cpu.temp", 72));

        Pixels.AssertNear(SKColors.Blue, At(frame, 102, 102));
        Pixels.AssertNear(SKColors.Black, At(frame, 98, 98), 0);
        Assert.True(Pixels.Count(frame, SKRectI.Create(100, 100, 300, 100), Pixels.IsBright) > 200);
    }

    [Fact]
    public void Above_rule_changes_the_style_when_the_value_crosses_it()
    {
        const string layout = Landscape + """
            styles:
              hot: { fill: "#FF0000" }
            widgets:
              - type: value
                at: [0, 0]
                size: [200, 100]
                source: cpu.temp
                style: { fill: "#0000FF" }
                when:
                  - { above: 85, style: hot }
            """;

        using var cool = Render(layout, ("cpu.temp", 70));
        using var hot = Render(layout, ("cpu.temp", 90));

        Pixels.AssertNear(SKColors.Blue, At(cool, 2, 2));
        Pixels.AssertNear(SKColors.Red, At(hot, 2, 2));
    }

    [Fact]
    public void Later_rules_merge_over_earlier_ones()
    {
        using var frame = Render(Landscape + """
            widgets:
              - type: value
                at: [0, 0]
                size: [200, 100]
                source: cpu.temp
                when:
                  - { above: 50, style: { fill: "#00FF00" } }
                  - { above: 80, style: { fill: "#FF0000" } }
            """, ("cpu.temp", 90));

        Pixels.AssertNear(SKColors.Red, At(frame, 2, 2));
    }

    [Fact]
    public void Missing_and_expired_values_are_stale_and_show_a_dash()
    {
        const string layout = Landscape + """
            widgets:
              - type: value
                at: [0, 0]
                size: [200, 100]
                source: cpu.temp
                style: { fill: "#0000FF", size: 60, align: center }
                when:
                  - { stale: true, style: { fill: "#404040" } }
            """;

        using var missing = Render(layout);
        var now = TestSensors.Time;
        var store = new SensorStore(() => now);
        store.Set("cpu.temp", 60, TimeSpan.FromSeconds(5));
        now = now.AddSeconds(6);
        using var expired = Frames.Render(layout, store.Snapshot());

        foreach (var frame in new[] { missing, expired })
        {
            Pixels.AssertNear(new SKColor(0x40, 0x40, 0x40), At(frame, 2, 2));
            // The dash is a short bright horizontal line at the vertical middle of the box.
            Assert.True(Pixels.Count(frame, SKRectI.Create(0, 30, 200, 40), Pixels.IsBright) > 50);
            Assert.Equal(0, Pixels.Count(frame, SKRectI.Create(0, 0, 200, 25), Pixels.IsBright));
        }
    }

    [Fact]
    public void Text_template_uses_its_style_color()
    {
        using var frame = Render(Landscape + """
            widgets:
              - type: text
                at: [0, 0]
                size: [600, 120]
                text: "{claude.sessions.busy}/{claude.sessions.total} working"
                style: { color: "#00FF00", size: 64 }
            """, ("claude.sessions.busy", 2), ("claude.sessions.total", 5));

        Assert.True(Pixels.Count(frame, SKRectI.Create(0, 0, 600, 120), c => Pixels.Near(c, SKColors.Lime, 30)) > 300);
    }

    [Fact]
    public void Alignment_places_text_inside_the_padding()
    {
        string Layout(string align) => Landscape + $$"""
            widgets:
              - type: text
                at: [0, 0]
                size: [1000, 100]
                text: "HI"
                style: { size: 60, align: {{align}}, padding: 20 }
            """;

        using var left = Render(Layout("left"));
        using var right = Render(Layout("right"));

        var leftHalf = SKRectI.Create(0, 0, 500, 100);
        var rightHalf = SKRectI.Create(500, 0, 500, 100);
        Assert.True(Pixels.Count(left, leftHalf, Pixels.IsBright) > 0);
        Assert.Equal(0, Pixels.Count(left, rightHalf, Pixels.IsBright));
        Assert.Equal(0, Pixels.Count(right, leftHalf, Pixels.IsBright));
        Assert.Equal(0, Pixels.Count(left, SKRectI.Create(0, 0, 18, 100), Pixels.IsBright));
        Assert.Equal(0, Pixels.Count(right, SKRectI.Create(982, 0, 18, 100), Pixels.IsBright));
    }

    [Fact]
    public void Pulse_dips_opacity_halfway_through_its_period()
    {
        const string layout = Landscape + """
            widgets:
              - type: text
                at: [0, 0]
                size: [200, 100]
                text: " "
                style:
                  fill: "#0000FF"
                  pulse: { property: opacity, period: 1000, min: 0.2 }
            """;
        using var folder = new TempTheme(layout);
        using var theme = folder.Load();

        using var start = Frames.Render(theme, TestSensors.Of(), TimeSpan.Zero);
        using var middle = Frames.Render(theme, TestSensors.Of(), TimeSpan.FromMilliseconds(500));
        using var end = Frames.Render(theme, TestSensors.Of(), TimeSpan.FromMilliseconds(1000));

        Pixels.AssertNear(new SKColor(0, 0, 255), At(start, 10, 10), 2);
        Pixels.AssertNear(new SKColor(0, 0, 51), At(middle, 10, 10), 2);
        Pixels.AssertNear(new SKColor(0, 0, 255), At(end, 10, 10), 2);
    }

    [Fact]
    public void Text_styles_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              label: { size: 28, color: "#9AA4B8", weight: semibold }
              big: { size: 96, weight: bold, color: "#FFFFFF" }
            background:
              - fill: { linear: ["#0B0F1A", "#1B2440"], angle: 90 }
            widgets:
              - { type: text, at: [40, 30], size: [400, 40], text: CPU, style: label }
              - type: value
                at: [40, 80]
                size: [400, 120]
                source: cpu.temp
                format: "{0:0}°C"
                style: { extends: big, glow: { color: "#FF5A36", radius: 18 } }
              - type: value
                at: [480, 80]
                size: [420, 120]
                source: gpu.temp
                format: "{0:0.0}°C"
                style:
                  extends: big
                  fill: { linear: ["#3A1C71", "#D76D77"] }
                  radius: 24
                  padding: 24
                  align: right
                  stroke: { width: 3, color: "#FFFFFF80" }
              - type: text
                at: [940, 80]
                size: [500, 120]
                text: "{claude.sessions.busy}/{claude.sessions.total} working"
                style: { size: 48, color: "#E0E6F0", shadow: { color: "#000000C0", x: 4, y: 4, blur: 6 }, align: center }
              - type: value
                at: [1480, 80]
                size: [400, 120]
                source: fps
                style: { extends: big, color: "#808080" }
                when:
                  - { stale: true, style: { fill: "#40202080", radius: 12 } }
            """, ("cpu.temp", 64.4), ("gpu.temp", 71.25), ("claude.sessions.busy", 2), ("claude.sessions.total", 5));

        Snapshots.Match(frame, "text-styles");
    }
}
