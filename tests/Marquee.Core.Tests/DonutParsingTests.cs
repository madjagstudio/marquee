using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class DonutParsingTests
{
    const string Header = """
        canvas: { orientation: landscape }
        styles:
          cpu: { fill: "#36D1DC" }
        widgets:

        """;

    static DonutWidget Donut(string widget) => Assert.IsType<DonutWidget>(Assert.Single(Layouts.Parse(Header + widget).Widgets));

    /// Line numbers in errors count from the top of the file; the header is 4 lines long, so the widget starts on line 5.
    static ThemeException Error(string widget) => Layouts.Error(Header + widget);

    static string Slices(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"    - {{ source: power.{i} }}\n"));

    [Fact]
    public void Donut_with_every_key()
    {
        var donut = Donut("""
            - type: donut
              at: [10, 20]
              size: [300, 300]
              hole: 0.5
              start: 0
              gap: 4
              labels: outside
              format: "{0:0}%"
              slices:
                - { source: cpu.power, label: CPU, style: cpu }
                - { source: gpu.power, label: GPU, style: { fill: "#FF5A36" } }
                - { source: ram.power }
            """);

        Assert.Equal(SKRect.Create(10, 20, 300, 300), donut.Bounds);
        Assert.Equal(0.5f, donut.Hole);
        Assert.Equal(0f, donut.StartAngle);
        Assert.Equal(4f, donut.Gap);
        Assert.Equal(DonutLabels.Outside, donut.Labels);
        Assert.Equal("{0:0}%", donut.Format);
        Assert.Equal(3, donut.Slices.Count);
        Assert.Equal("CPU", donut.Slices[0].Label);
        Assert.Equal(new SolidPaint(new SKColor(0x36, 0xD1, 0xDC)), donut.Slices[0].Style.Fill);
        Assert.Equal(new SolidPaint(new SKColor(0xFF, 0x5A, 0x36)), donut.Slices[1].Style.Fill);
        Assert.Equal(40, donut.Slices[1].Source.Evaluate(TestSensors.Of(("gpu.power", 40)))?.Number);
        Assert.Null(donut.Slices[2].Label);
        Assert.Same(StyleSpec.Empty, donut.Slices[2].Style);
        Assert.Null(donut.Source);
    }

    [Fact]
    public void Donut_defaults()
    {
        var donut = Donut("""
            - type: donut
              at: [0, 0]
              size: [100, 100]
              slices:
                - { source: a }
                - { source: b }
            """);

        Assert.Equal(DonutWidget.DefaultHole, donut.Hole);
        Assert.Equal(0.6f, donut.Hole);
        Assert.Equal(-90f, donut.StartAngle);
        Assert.Equal(2f, donut.Gap);
        Assert.Equal(DonutLabels.None, donut.Labels);
        Assert.Null(donut.Format);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.9)]
    public void Hole_takes_0_to_0_9(double hole)
    {
        var donut = Donut($$"""
            - type: donut
              at: [0, 0]
              size: [100, 100]
              hole: {{hole}}
              slices: [{ source: a }, { source: b }]
            """);

        Assert.Equal((float)hole, donut.Hole);
    }

    [Fact]
    public void Twelve_slices_parse() =>
        Assert.Equal(12, Donut("- type: donut\n  at: [0, 0]\n  size: [100, 100]\n  slices:\n" + Slices(12)).Slices.Count);

    [Theory]
    [InlineData(1)]
    [InlineData(13)]
    public void Slice_count_is_2_to_12(int count)
    {
        var ex = Error("- type: donut\n  at: [0, 0]\n  size: [100, 100]\n  slices:\n" + Slices(count));

        Assert.Equal($"slices must have between 2 and 12 entries, not {count}", ex.Reason);
        Assert.Equal(9, ex.Line);
    }

    const string TwoSlices = "  slices: [{ source: a }, { source: b }]";

    [Theory]
    [InlineData("  hole: -0.1", 9, "hole must be between 0 and 0.9")]
    [InlineData("  hole: 0.95", 9, "hole must be between 0 and 0.9")]
    [InlineData("  labels: around", 9, "labels must be one of: outside, inside, none; not 'around'")]
    [InlineData("  gap: 91", 9, "gap must be between 0 and 90")]
    [InlineData("  start: 400", 9, "start must be between -360 and 360")]
    [InlineData("  format: \"%\"", 9, "format \"%\" never shows the value; include {0}, e.g. \"{0:0.0}°C\"")]
    public void Donut_key_errors(string line, int at, string reason)
    {
        var ex = Error($"- type: donut\n  at: [0, 0]\n  size: [100, 100]\n{TwoSlices}\n{line}\n");

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(at, ex.Line);
    }

    [Theory]
    [InlineData("{ label: CPU }", "donut slice is missing 'source'")]
    [InlineData("{ source: a, color: red }", "Unknown key 'color' in donut slice. Expected one of: source, label, style")]
    [InlineData("{ source: a + }", "source: Expression ended unexpectedly at character 4")]
    [InlineData("{ source: a, style: nope }", "Unknown style 'nope'. Defined styles: cpu")]
    [InlineData("{ source: a, label: [x] }", "label must be a single value, not a list or mapping")]
    [InlineData("cpu.power", "donut slice must be a mapping of keys to values")]
    public void Slice_errors(string slice, string reason)
    {
        var ex = Error($$"""
            - type: donut
              at: [0, 0]
              size: [100, 100]
              slices:
                - { source: b }
                - {{slice}}
            """);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(10, ex.Line);
    }

    [Fact]
    public void Slices_must_be_a_list()
    {
        var ex = Error("- type: donut\n  at: [0, 0]\n  size: [100, 100]\n  slices: { source: a }\n");

        Assert.Equal("slices must be a list", ex.Reason);
        Assert.Equal(8, ex.Line);
    }

    [Fact]
    public void Missing_slices_is_an_error()
    {
        var ex = Error("- { type: donut, at: [0, 0], size: [100, 100] }");

        Assert.Equal("donut widget is missing 'slices'", ex.Reason);
        Assert.Equal(5, ex.Line);
    }

    /// `marquee validate` reports a mistake in each slice, not just the first, and the widget's siblings after it.
    [Fact]
    public void Validate_reports_every_bad_slice()
    {
        var (spec, errors) = Layouts.Collect(Header + """
            - type: donut
              at: [0, 0]
              size: [100, 100]
              slices:
                - { source: a, colour: red }
                - { label: B }
                - { source: c }
            - { type: text, at: [0, 0], size: [10, 10], text: ok, nope: 1 }
            """);

        Assert.Equal(
        [
            "9: Unknown key 'colour' in donut slice. Expected one of: source, label, style",
            "10: donut slice is missing 'source'",
            "12: Unknown key 'nope' in text widget. Expected one of: type, at, size, source, style, when, smooth, rotate, hidden, locked, name, text, spans",
        ], Layouts.Lines(errors));
        Assert.Empty(spec.Widgets);
    }
}
