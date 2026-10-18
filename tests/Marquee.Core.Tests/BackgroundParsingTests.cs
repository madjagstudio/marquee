using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class BackgroundParsingTests
{
    static IReadOnlyList<BackgroundLayer> Layers(string background) =>
        Layouts.Parse($"canvas: {{ orientation: landscape }}\nbackground:\n{background}").Background;

    static ThemeException LayerError(string background) =>
        Layouts.Error($"canvas: {{ orientation: landscape }}\nbackground:\n{background}");

    [Fact]
    public void Layers_keep_their_order_and_defaults()
    {
        var layers = Layers("""
              - fill: "#101018"
              - image: art/sky.png
            """);

        var fill = Assert.IsType<FillLayer>(layers[0]);
        Assert.Equal(new SolidPaint(new SKColor(0x10, 0x10, 0x18)), fill.Paint);
        Assert.Equal(1f, fill.Opacity);
        Assert.Equal(SKBlendMode.SrcOver, fill.Blend);
        Assert.Equal(new ImageLayer("art/sky.png", ImageFit.Cover, 1f, SKBlendMode.SrcOver), layers[1]);
    }

    [Fact]
    public void At_most_16_layers()
    {
        Assert.Equal(16, Layers(string.Concat(Enumerable.Repeat("  - fill: \"#000\"\n", 16))).Count);

        var ex = LayerError(string.Concat(Enumerable.Repeat("  - fill: \"#000\"\n", 17)));

        Assert.Equal("a background can have at most 16 layers", ex.Reason);
        Assert.Equal(19, ex.Line); // the 17th layer
    }

    [Fact]
    public void Image_layer_options()
    {
        var layer = Layers("""
              - image: sky.png
                fit: contain
                opacity: 0.5
                blend: screen
            """)[0];

        Assert.Equal(new ImageLayer("sky.png", ImageFit.Contain, 0.5f, SKBlendMode.Screen), layer);
    }

    [Fact]
    public void Linear_gradient_with_angle()
    {
        var fill = (FillLayer)Layers("""
              - fill: { linear: ["#000000", "#2040FF"], angle: 90 }
            """)[0];

        var gradient = Assert.IsType<LinearGradientPaint>(fill.Paint);
        Assert.Equal(new[] { SKColors.Black, new SKColor(0x20, 0x40, 0xFF) }, gradient.Colors);
        Assert.Equal(90f, gradient.Angle);
    }

    [Fact]
    public void Radial_gradient()
    {
        var fill = (FillLayer)Layers("""
              - fill: { radial: ["#FFFFFF", "#00000000", "#000"] }
            """)[0];

        Assert.Equal(3, Assert.IsType<RadialGradientPaint>(fill.Paint).Colors.Length);
    }

    [Theory]
    [InlineData("  - video: loop.mp4", 3, "video background layers arrive in a later version of Marquee; use fill, image or shader")]
    [InlineData("  - opacity: 1", 3, "background layer needs one of: fill, image, shader, video")]
    [InlineData("  - fill: \"#000\"\n    image: a.png", 3, "background layer can only have one of: fill, image")]
    [InlineData("  - fill: #000000", 3, "fill is empty. Quote colors, e.g. \"#FF0000\": an unquoted # starts a YAML comment")]
    [InlineData("  - fill: red", 3, "fill: 'red' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"")]
    [InlineData("  - fill: \"#000\"\n    opacity: 2", 4, "background layer opacity must be between 0 and 1")]
    [InlineData("  - fill: \"#000\"\n    blend: burn", 4, "blend must be one of: normal, multiply, screen, overlay, darken, lighten, add; not 'burn'")]
    [InlineData("  - fill: \"#000\"\n    fit: cover", 4, "fit only applies to image layers")]
    [InlineData("  - image: a.png\n    fit: tile", 4, "fit must be one of: cover, contain, stretch, none; not 'tile'")]
    [InlineData("  - fill: { linear: [\"#000\"] }", 3, "fill.linear needs at least two colors")]
    [InlineData("  - fill: { radial: [\"#000\", \"#fff\"], angle: 45 }", 3, "fill: angle only applies to linear gradients")]
    [InlineData("  - fill: { linear: [\"#000\", \"#fff\"], angle: 1e39 }", 3, "fill.angle is too large")]
    [InlineData("  - fill: { conic: [\"#000\", \"#fff\"] }", 3, "Unknown key 'conic' in fill. Expected one of: linear, radial, angle")]
    [InlineData("  - fill: \"#000\"\n    size: 3", 4, "Unknown key 'size' in background layer. Expected one of: fill, image, shader, video, fit, uniforms, scale, opacity, blend")]
    public void Layer_errors(string background, int line, string reason)
    {
        var ex = LayerError(background);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Fact]
    public void Background_must_be_a_list() =>
        Assert.Equal("background must be a list", Layouts.Error("canvas: { orientation: landscape }\nbackground: \"#000\"").Reason);

    [Theory]
    [InlineData("#F00", 255, 0, 0, 255)]
    [InlineData("#00FF00", 0, 255, 0, 255)]
    [InlineData("#0000FF80", 0, 0, 255, 0x80)]
    [InlineData("#abcdef", 0xAB, 0xCD, 0xEF, 255)]
    [InlineData("transparent", 0, 0, 0, 0)]
    public void Colors_are_css_hex(string text, int r, int g, int b, int a)
    {
        Assert.True(PaintParser.TryParseColor(text, out var color));
        Assert.Equal(new SKColor((byte)r, (byte)g, (byte)b, (byte)a), color);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("FF0000")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Invalid_colors_are_rejected(string text) => Assert.False(PaintParser.TryParseColor(text, out _));
}
