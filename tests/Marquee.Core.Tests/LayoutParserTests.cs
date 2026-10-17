using Marquee.Core.Devices;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

public class LayoutParserTests
{
    [Fact]
    public void Canvas_declares_orientation_and_defaults_to_30_fps()
    {
        var spec = Layouts.Parse("canvas: { orientation: landscape }");

        Assert.Equal(ThemeOrientation.Landscape, spec.Canvas.Orientation);
        Assert.Equal(30, spec.Canvas.Fps);
        Assert.Equal(new SkiaSharp.SKSizeI(1920, 480), spec.Canvas.Size);
        Assert.Empty(spec.Background);
    }

    [Fact]
    public void Portrait_canvas_with_fps()
    {
        var spec = Layouts.Parse("""
            canvas:
              orientation: portrait
              fps: 60
            """);

        Assert.Equal(new SkiaSharp.SKSizeI(480, 1920), spec.Canvas.Size);
        Assert.Equal(60, spec.Canvas.Fps);
    }

    [Theory]
    [InlineData(ThemeOrientation.Landscape, Orientation.Landscape, true)]
    [InlineData(ThemeOrientation.Landscape, Orientation.LandscapeFlipped, true)]
    [InlineData(ThemeOrientation.Landscape, Orientation.Portrait, false)]
    [InlineData(ThemeOrientation.Portrait, Orientation.PortraitFlipped, true)]
    [InlineData(ThemeOrientation.Portrait, Orientation.Landscape, false)]
    public void A_theme_fits_both_variants_of_its_orientation_family(ThemeOrientation theme, Orientation device, bool fits) =>
        Assert.Equal(fits, new CanvasSpec(theme, 30).Fits(device));

    [Theory]
    [InlineData("", 0, "layout.yaml is empty. It needs at least a canvas section.")]
    [InlineData("# just a comment\n", 0, "layout.yaml is empty. It needs at least a canvas section.")]
    [InlineData("canvas: landscape", 1, "canvas must be a mapping of keys to values")]
    [InlineData("background: []", 1, "layout.yaml is missing 'canvas'")]
    [InlineData("canvas: { orientation: sideways }", 1, "canvas.orientation must be one of: portrait, landscape; not 'sideways'")]
    [InlineData("canvas: { fps: 30 }", 1, "canvas is missing 'orientation'")]
    [InlineData("canvas:\n  orientation: landscape\n  fps: 120", 3, "canvas.fps must be between 1 and 60")]
    [InlineData("canvas:\n  orientation: landscape\n  fps: 29.5", 3, "canvas.fps must be a whole number")]
    [InlineData("canvas:\n  orientation: landscape\n  fps: fast", 3, "canvas.fps must be a number, not 'fast'")]
    [InlineData("canvas:\n  orientation: landscape\n  orientation: portrait", 3, "YAML syntax error: Duplicate key orientation")]
    public void Errors_name_the_line_and_the_problem(string yaml, int line, string reason)
    {
        var ex = Layouts.Error(yaml);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Fact]
    public void Unknown_top_level_keys_are_errors()
    {
        var ex = Layouts.Error("canvas: { orientation: landscape }\nwidget: []");

        Assert.StartsWith("Unknown key 'widget' in layout.yaml. Expected one of: canvas,", ex.Reason);
        Assert.Equal(2, ex.Line);
    }

    [Fact]
    public void Syntax_errors_carry_the_line_from_the_yaml_parser()
    {
        var ex = Layouts.Error("""
            canvas:
              orientation: landscape
             fps: 30
            """);

        Assert.StartsWith("YAML syntax error:", ex.Reason);
        Assert.Equal(3, ex.Line);
    }

    [Fact]
    public void Unclosed_flow_list_is_still_a_theme_error() =>
        Assert.StartsWith("YAML syntax error:", Layouts.Error("canvas: { orientation: landscape }\nbackground: [1, 2").Reason);

    [Fact]
    public void Message_leads_with_file_and_line() =>
        Assert.Equal("layout.yaml line 1: canvas must be a mapping of keys to values", Layouts.Error("canvas: 3").Message);
}
