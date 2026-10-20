using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A gradient's `stops`, and a radial gradient's `center` and `radius`, as drawn.
public class GradientRenderingTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    static SKImage Box(string fill) => Frames.Render(Landscape + $$"""
        widgets:
          - { type: shape, shape: rect, at: [100, 100], size: [400, 200], style: { fill: {{fill}} } }
        """, TestSensors.Of());

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    static SKColor[] PixelsOf(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Pixels;
    }

    [Fact]
    public void Equal_stops_make_a_hard_edge()
    {
        using var frame = Box("""{ linear: ["#FF0000", "#FF0000", "#0000FF", "#0000FF"], stops: [0, 0.5, 0.5, 1] }""");

        Pixels.AssertNear(SKColors.Red, At(frame, 101, 200), 0);
        Pixels.AssertNear(SKColors.Red, At(frame, 298, 200), 0);
        Pixels.AssertNear(SKColors.Blue, At(frame, 302, 200), 0);
        Pixels.AssertNear(SKColors.Blue, At(frame, 498, 200), 0);
    }

    [Fact]
    public void Stops_move_where_each_color_sits()
    {
        // Black to white with white at a quarter: past it the box is white, where an even spread is still grey.
        using var frame = Box("""{ linear: ["#000000", "#FFFFFF"], stops: [0, 0.25] }""");

        Pixels.AssertNear(SKColors.White, At(frame, 250, 200), 0);
        Pixels.AssertNear(new SKColor(0x80, 0x80, 0x80), At(frame, 150, 200), 4);
    }

    [Fact]
    public void Center_and_radius_place_a_radial_gradient()
    {
        // The centre is a quarter across the 400 × 200 box; the radius is half of half its longer side, 100 px.
        using var frame = Box("""{ radial: ["#FFFFFF", "#000000"], center: [0.25, 0.5], radius: 0.5 }""");

        Pixels.AssertNear(SKColors.White, At(frame, 200, 200), 2);
        Pixels.AssertNear(new SKColor(0x80, 0x80, 0x80), At(frame, 250, 200), 4);
        Pixels.AssertNear(SKColors.Black, At(frame, 302, 200), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 290, 260), 0); // 108 px from the centre
    }

    [Fact]
    public void A_radius_of_3_from_a_corner_reaches_the_far_corner()
    {
        // From the 400 × 200 box's top-left corner, radius 3 is 600 px, where radius 1 turns black halfway along the top
        // edge. The bottom-right corner, √(400² + 200²) ≈ 447 px away, is still inside the gradient, not its last color.
        using var frame = Box("""{ radial: ["#FFFFFF", "#000000"], center: [0, 0], radius: 3 }""");

        Pixels.AssertNear(SKColors.White, At(frame, 100, 100), 2);
        Pixels.AssertNear(new SKColor(0xA0, 0xA0, 0xA0), At(frame, 300, 200), 4); // 224 px out
        Pixels.AssertNear(new SKColor(0x7F, 0x7F, 0x7F), At(frame, 400, 101), 4); // 300 px out
        Pixels.AssertNear(new SKColor(0x41, 0x41, 0x41), At(frame, 499, 299), 4); // 447 px out
    }

    [Fact]
    public void A_radius_of_0_paints_the_last_color()
    {
        using var frame = Box("""{ radial: ["#FFFFFF", "#FF0000"], radius: 0 }""");

        Pixels.AssertNear(SKColors.Red, At(frame, 300, 200), 0);
        Pixels.AssertNear(SKColors.Red, At(frame, 101, 101), 0);
    }

    [Theory]
    [InlineData("""{ linear: ["#36D1DC", "#FF5A36"], angle: 30 }""", """{ linear: ["#36D1DC", "#FF5A36"], angle: 30, stops: [0, 1] }""")]
    [InlineData("""{ radial: ["#A770EF", "#1E2A44"] }""", """{ radial: ["#A770EF", "#1E2A44"], stops: [0, 1], center: [0.5, 0.5], radius: 1 }""")]
    public void Writing_the_defaults_draws_what_leaving_them_out_does(string omitted, string written)
    {
        using var left = Box(omitted);
        using var right = Box(written);

        Assert.True(PixelsOf(left).SequenceEqual(PixelsOf(right)), "writing the defaults changed the picture");
    }

    [Fact]
    public void Stops_and_an_off_centre_radial_on_boxes_and_bars()
    {
        using var frame = Frames.Render(Landscape + """
            background:
              - fill: "#0B0F1A"
            widgets:
              - type: shape
                shape: rect
                at: [40, 40]
                size: [560, 180]
                style: { fill: { linear: ["#000000", "#36D1DC", "#FFFFFF"], stops: [0, 0.8, 1], angle: 90 }, radius: 24 }
              - type: shape
                shape: rect
                at: [640, 40]
                size: [560, 180]
                style: { fill: { linear: ["#3DDC84", "#3DDC84", "#F6D743", "#F6D743", "#FF5A36"], stops: [0, 0.4, 0.4, 0.7, 1] }, radius: 24 }
              - type: shape
                shape: rect
                at: [1240, 40]
                size: [640, 180]
                style: { fill: { radial: ["#FFFFFF60", "#36D1DC30", "#00000000"], stops: [0, 0.3, 1], center: [0.3, 0.4], radius: 0.8 }, stroke: { width: 2, color: "#FFFFFF30" }, radius: 24 }
              - type: bar
                at: [40, 280]
                size: [880, 48]
                source: cpu.load
                style: { fill: { linear: ["#3DDC84", "#3DDC84", "#FF5A36", "#FF5A36"], stops: [0, 0.6, 0.6, 1] }, track: "#FFFFFF18", radius: 24 }
              - type: bar
                at: [1000, 280]
                size: [880, 48]
                source: gpu.load
                style: { fill: { radial: ["#FFFFFF", "#A770EF", "#1E2A44"], stops: [0, 0.2, 1], center: [0.1, 0.5], radius: 0.6 }, track: { linear: ["#FFFFFF08", "#FFFFFF30"], stops: [0.5, 1] }, radius: 24 }
              - type: bar
                at: [40, 380]
                size: [1840, 48]
                source: ram.used_pct
                style: { fill: { radial: ["#F2C94C", "#B0306A"], center: [1, 0.5], radius: 0.5 }, track: "#FFFFFF18", radius: 8 }
            """, TestSensors.Of(("cpu.load", 80), ("gpu.load", 70), ("ram.used_pct", 100)));

        Snapshots.Match(frame, "gradient-stops-and-centre");
    }
}
