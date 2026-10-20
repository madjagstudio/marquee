using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class ShapeParsingTests
{
    const string Header = """
        canvas: { orientation: landscape }
        styles:
          line: { stroke: { width: 2, color: "#FFFFFF" } }
          hot: { stroke: { color: "#FF0000" } }
        widgets:

        """;

    static WidgetSpec Widget(string widget) => Assert.Single(Layouts.Parse(Header + widget).Widgets);

    /// Line numbers in errors count from the top of the file; the header is 5 lines long.
    static ThemeException Error(string widgets) => Layouts.Error(Header + widgets);

    [Theory]
    [InlineData("rect", ShapeKind.Rect)]
    [InlineData("ellipse", ShapeKind.Ellipse)]
    [InlineData("line", ShapeKind.Line)]
    [InlineData("arc", ShapeKind.Arc)]
    public void Each_shape_parses(string name, ShapeKind kind)
    {
        var shape = Assert.IsType<ShapeWidget>(Widget($"- {{ type: shape, shape: {name}, at: [10, 20], size: [300, 40], style: line }}"));

        Assert.Equal(kind, shape.Shape);
        Assert.Equal(SKRect.Create(10, 20, 300, 40), shape.Bounds);
    }

    [Fact]
    public void Arc_takes_a_gauges_defaults_and_its_own_angles()
    {
        var plain = Assert.IsType<ShapeWidget>(Widget("- { type: shape, shape: arc, at: [0, 0], size: [100, 100] }"));
        var set = Assert.IsType<ShapeWidget>(Widget(
            "- { type: shape, shape: arc, at: [0, 0], size: [100, 100], start: -90, sweep: -180, thickness: 30 }"));

        Assert.Equal((135f, 270f, 12f), (plain.StartAngle, plain.SweepAngle, plain.Thickness));
        Assert.Equal((-90f, -180f, 30f), (set.StartAngle, set.SweepAngle, set.Thickness));
    }

    [Fact]
    public void Shape_takes_a_source_and_when_rules_like_any_widget()
    {
        var shape = Assert.IsType<ShapeWidget>(Widget("""
            - type: shape
              shape: line
              at: [0, 0]
              size: [100, 0]
              source: cpu.temp
              style: line
              when:
                - { above: 80, style: hot }
            """));

        Assert.NotNull(shape.Source);
        Assert.Equal(SKColors.Red, Assert.IsType<SolidPaint>(Assert.Single(shape.When).Style.Stroke!.Paint).Color);
    }

    [Theory]
    [InlineData("[300, 0]")]
    [InlineData("[0, 300]")]
    [InlineData("[0.5, 300]")]
    public void Line_may_be_less_than_1_wide_or_high(string size)
    {
        var line = Assert.IsType<ShapeWidget>(Widget($"- {{ type: shape, shape: line, at: [10, 20], size: {size}, style: line }}"));

        Assert.Equal(ShapeKind.Line, line.Shape);
    }

    /// A line's stroke may come from a rule alone: then the line shows only while the rule holds.
    [Fact]
    public void Line_stroked_only_by_a_when_rule_parses()
    {
        var line = Assert.IsType<ShapeWidget>(Widget(
            "- { type: shape, shape: line, at: [0, 0], size: [100, 0], source: cpu.temp, when: [{ above: 80, style: hot }] }"));

        Assert.Null(line.Style.Stroke);
    }

    [Theory]
    [InlineData("- { type: shape, at: [0, 0], size: [10, 10] }", 6, "shape widget is missing 'shape'")]
    [InlineData("- { type: shape, shape: star, at: [0, 0], size: [10, 10] }", 6, "shape must be one of: rect, ellipse, line, arc; not 'star'")]
    [InlineData("- { type: shape, shape: [rect], at: [0, 0], size: [10, 10] }", 6, "shape must be a single value, not a list or mapping")]
    [InlineData("- type: shape\n  shape: rect\n  at: [0, 0]\n  size: [10, 10]\n  start: 90", 10,
        "start only applies to an arc, and this shape is a rect")]
    [InlineData("- type: shape\n  shape: ellipse\n  at: [0, 0]\n  size: [10, 10]\n  sweep: 90", 10,
        "sweep only applies to an arc, and this shape is an ellipse")]
    [InlineData("- type: shape\n  shape: line\n  at: [0, 0]\n  size: [10, 0]\n  style: line\n  thickness: 4", 11,
        "thickness only applies to an arc, and this shape is a line")]
    [InlineData("- { type: shape, shape: arc, at: [0, 0], size: [10, 10], sweep: 0 }", 6, "sweep must not be 0")]
    [InlineData("- { type: shape, shape: arc, at: [0, 0], size: [10, 10], sweep: 361 }", 6, "sweep must be between -360 and 360")]
    [InlineData("- { type: shape, shape: arc, at: [0, 0], size: [10, 10], start: -361 }", 6, "start must be between -360 and 360")]
    [InlineData("- { type: shape, shape: arc, at: [0, 0], size: [10, 10], thickness: 0.5 }", 6, "thickness must be at least 1")]
    [InlineData("- { type: shape, shape: rect, at: [0, 0], size: [10, 10], format: x }", 6,
        "Unknown key 'format' in shape widget. Expected one of: type, at, size, source, style, when, smooth, rotate, hidden, locked, name, shape, start, sweep, thickness")]
    public void Shape_errors(string widgets, int line, string reason)
    {
        var ex = Error(widgets);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Theory]
    [InlineData("rect", "[0, 10]")]
    [InlineData("rect", "[10, 0.5]")]
    [InlineData("ellipse", "[10, 0]")]
    [InlineData("arc", "[0, 10]")]
    [InlineData("rect", "[-1, 5]")]
    [InlineData("ellipse", "[5, -3]")]
    public void Only_a_line_may_be_less_than_1_wide_or_high(string shape, string size)
    {
        var ex = Error($"- {{ type: shape, shape: {shape}, at: [0, 0], size: {size} }}");

        Assert.Equal("size must be at least 1", ex.Reason);
        Assert.Equal(6, ex.Line);
    }

    [Theory]
    [InlineData("[0, 0]", "A line needs a length: its size can't be 0 both ways")]
    [InlineData("[-1, 10]", "size must be at least 0")]
    public void Line_size_errors(string size, string reason)
    {
        var ex = Error($"- {{ type: shape, shape: line, at: [0, 0], size: {size}, style: line }}");

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(6, ex.Line);
    }

    /// It would draw nothing, so it's an error rather than a puzzle on the screen.
    [Theory]
    [InlineData("")]
    [InlineData(", style: { color: \"#FFFFFF\" }")]
    [InlineData(", style: { fill: \"#FFFFFF\", stroke: { width: 4 } }")]
    [InlineData(", source: cpu.temp, when: [{ above: 80, style: { fill: \"#FF0000\" } }]")]
    public void Line_without_a_stroke_color_is_an_error(string rest)
    {
        var ex = Error($"- {{ type: shape, shape: line, at: [0, 0], size: [100, 0]{rest} }}");

        Assert.Equal("A line is drawn by its stroke alone, but nothing gives it a stroke color: set one in its style, "
            + "such as stroke: { width: 2, color: \"#FFFFFF\" }", ex.Reason);
        Assert.Equal(6, ex.Line);
    }

    /// `marquee validate` reports a bad shape and still checks the widgets after it.
    [Fact]
    public void Collecting_reports_each_bad_shape()
    {
        var (spec, errors) = Layouts.Collect(Header + """
            - { type: shape, shape: rect, at: [0, 0], size: [10, 10], thickness: 3 }
            - { type: shape, shape: line, at: [0, 0], size: [10, 0] }
            - { type: shape, shape: ellipse, at: [0, 0], size: [10, 10] }
            """);

        Assert.Equal(
        [
            "6: thickness only applies to an arc, and this shape is a rect",
            "7: A line is drawn by its stroke alone, but nothing gives it a stroke color: set one in its style, "
                + "such as stroke: { width: 2, color: \"#FFFFFF\" }",
        ], Layouts.Lines(errors));
        Assert.Equal(ShapeKind.Ellipse, Assert.IsType<ShapeWidget>(Assert.Single(spec.Widgets)).Shape);
    }
}
