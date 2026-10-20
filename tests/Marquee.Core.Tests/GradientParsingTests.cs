using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A gradient's `stops`, and a radial gradient's `center` and `radius`.
public class GradientParsingTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    static PaintSpec Fill(string fill) => ((FillLayer)Layouts.Parse($"{Landscape}background:\n  - fill: {fill}\n").Background[0]).Paint;

    static ThemeException FillError(string fill) => Layouts.Error($"{Landscape}background:\n  - fill: {fill}\n");

    [Fact]
    public void Without_the_new_keys_colors_spread_evenly_from_the_centre()
    {
        var linear = Assert.IsType<LinearGradientPaint>(Fill("""{ linear: ["#000", "#fff"] }"""));
        var radial = Assert.IsType<RadialGradientPaint>(Fill("""{ radial: ["#000", "#fff"] }"""));

        Assert.Null(linear.Stops);
        Assert.Null(radial.Stops);
        Assert.Equal(new SKPoint(0.5f, 0.5f), radial.Center);
        Assert.Equal(1f, radial.Radius);
    }

    [Fact]
    public void Stops_place_each_color_on_both_kinds()
    {
        var linear = Assert.IsType<LinearGradientPaint>(Fill("""{ linear: ["#000000", "#36D1DC", "#FFFFFF"], stops: [0, 0.8, 1], angle: 90 }"""));
        var radial = Assert.IsType<RadialGradientPaint>(Fill("""{ radial: ["#000", "#fff"], stops: [0.25, 0.75] }"""));

        Assert.Equal([0f, 0.8f, 1f], linear.Stops!);
        Assert.Equal(90f, linear.Angle);
        Assert.Equal([0.25f, 0.75f], radial.Stops!);
    }

    [Fact]
    public void Equal_neighbours_make_a_hard_edge_and_the_bounds_are_allowed()
    {
        var linear = Assert.IsType<LinearGradientPaint>(Fill("""{ linear: ["#000", "#000", "#fff", "#fff"], stops: [0, 0.5, 0.5, 1] }"""));
        var pinned = Assert.IsType<LinearGradientPaint>(Fill("""{ linear: ["#000", "#fff"], stops: [1, 1] }"""));

        Assert.Equal([0f, 0.5f, 0.5f, 1f], linear.Stops!);
        Assert.Equal([1f, 1f], pinned.Stops!);
    }

    [Fact]
    public void Center_and_radius_move_and_size_a_radial_gradient()
    {
        var radial = Assert.IsType<RadialGradientPaint>(Fill("""{ radial: ["#FFFFFF60", "#00000000"], center: [0.3, 0.4], radius: 0.8 }"""));

        Assert.Equal(new SKPoint(0.3f, 0.4f), radial.Center);
        Assert.Equal(0.8f, radial.Radius);
    }

    [Theory]
    [InlineData("[0, 0]", 0)]
    [InlineData("[1, 1]", 1)]
    public void Center_and_radius_take_0_and_1(string center, float radius)
    {
        var radial = Assert.IsType<RadialGradientPaint>(Fill($$"""{ radial: ["#000", "#fff"], center: {{center}}, radius: {{radius}} }"""));

        Assert.Equal(new SKPoint(radius, radius), radial.Center);
        Assert.Equal(radius, radial.Radius);
    }

    /// Up to 3, so a glow from any centre, a corner included, can cover the whole area.
    [Fact]
    public void A_radius_takes_up_to_3()
    {
        var radial = Assert.IsType<RadialGradientPaint>(Fill("""{ radial: ["#000", "#fff"], center: [0, 0], radius: 3 }"""));

        Assert.Equal(3f, radial.Radius);
    }

    [Theory]
    [InlineData("""{ linear: ["#000", "#fff", "#888"], stops: [0, 1] }""", "fill.stops needs 3 positions, one per color, not 2")]
    [InlineData("""{ radial: ["#000", "#fff"], stops: [0, 0.5, 1] }""", "fill.stops needs 2 positions, one per color, not 3")]
    [InlineData("""{ linear: ["#000", "#fff", "#888"], stops: [0, 0.8, 0.5] }""", "fill.stops must rise: 0.5 comes after 0.8")]
    [InlineData("""{ linear: ["#000", "#fff"], stops: [-0.01, 1] }""", "fill.stops must be between 0 and 1")]
    [InlineData("""{ linear: ["#000", "#fff"], stops: [0, 1.01] }""", "fill.stops must be between 0 and 1")]
    [InlineData("""{ linear: ["#000", "#fff"], stops: 0.5 }""", "fill.stops must be a list")]
    [InlineData("""{ radial: ["#000", "#fff"], center: [-0.01, 0.5] }""", "fill.center's x must be between 0 and 1")]
    [InlineData("""{ radial: ["#000", "#fff"], center: [0.5, 1.01] }""", "fill.center's y must be between 0 and 1")]
    [InlineData("""{ radial: ["#000", "#fff"], center: [0.5] }""", "fill.center must be a pair of fractions like [0.5, 0.5]")]
    [InlineData("""{ radial: ["#000", "#fff"], center: 0.5 }""", "fill.center must be a pair of fractions like [0.5, 0.5]")]
    [InlineData("""{ radial: ["#000", "#fff"], radius: -0.01 }""", "fill.radius must be between 0 and 3")]
    [InlineData("""{ radial: ["#000", "#fff"], radius: 3.01 }""", "fill.radius must be between 0 and 3")]
    [InlineData("""{ linear: ["#000", "#fff"], center: [0.5, 0.5] }""", "fill: center only applies to radial gradients")]
    [InlineData("""{ linear: ["#000", "#fff"], radius: 1 }""", "fill: radius only applies to radial gradients")]
    public void Bad_stops_center_and_radius_are_errors(string fill, string reason)
    {
        var ex = FillError(fill);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(3, ex.Line);
    }

    [Fact]
    public void A_falling_stop_is_reported_on_its_own_line()
    {
        var ex = FillError("""

                  linear: ["#000", "#888", "#fff"]
                  stops:
                    - 0
                    - 0.6
                    - 0.4
            """);

        Assert.Equal("fill.stops must rise: 0.4 comes after 0.6", ex.Reason);
        Assert.Equal(8, ex.Line);
    }

    [Fact]
    public void Every_paint_takes_the_new_keys_and_validate_reports_each_bad_one()
    {
        var (spec, errors) = Layouts.Collect(Landscape + """
            widgets:
              - type: shape
                shape: rect
                at: [0, 0]
                size: [100, 100]
                style: { stroke: { width: 2, color: { linear: ["#000", "#fff"], stops: [0, 0.2] } }, fill: { radial: ["#000", "#fff"], center: [0, 1] } }
              - { type: bar, at: [0, 0], size: [100, 10], source: cpu.load, style: { track: { linear: ["#000", "#fff"], stops: [1, 0] } } }
              - { type: bar, at: [0, 0], size: [100, 10], source: cpu.load, style: { fill: { radial: ["#000", "#fff"], radius: 3.01 } } }
            """);

        Assert.Equal(["8: track.stops must rise: 0 comes after 1", "9: fill.radius must be between 0 and 3"], Layouts.Lines(errors));
        var style = spec.Widgets[0].Style;
        Assert.Equal([0f, 0.2f], Assert.IsType<LinearGradientPaint>(style.Stroke!.Paint).Stops!);
        Assert.Equal(new SKPoint(0, 1), Assert.IsType<RadialGradientPaint>(style.Fill).Center);
    }
}
