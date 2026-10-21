using System.Windows.Media;
using Marquee.Editor.Controls;

namespace Marquee.Editor.Tests;

/// The colour picker's maths: hue, saturation, value and alpha to and from a colour, and the hex text layout.yaml
/// writes, with the alpha last.
public class ColorMathTests
{
    [Theory]
    [InlineData("#FF0000", 0, 1, 1)]
    [InlineData("#00FF00", 120, 1, 1)]
    [InlineData("#0000FF", 240, 1, 1)]
    [InlineData("#FFFF00", 60, 1, 1)]
    [InlineData("#FF00FF", 300, 1, 1)]
    [InlineData("#808080", 0, 0, 0.50196)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 0, 0, 1)]
    public void A_colour_has_the_hue_saturation_and_value_of_its_place_on_the_wheel(string hex, double h, double s, double v)
    {
        Assert.True(ColorText.TryParse(hex, out var color));

        var hsva = Hsva.FromColor(color);

        Assert.Equal(h, hsva.H, 3);
        Assert.Equal(s, hsva.S, 3);
        Assert.Equal(v, hsva.V, 3);
        Assert.Equal(1, hsva.A, 3);
    }

    [Fact]
    public void Every_colour_comes_back_the_same_through_hsv()
    {
        var random = new Random(14);
        for (var i = 0; i < 2000; i++)
        {
            var color = Color.FromArgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));

            Assert.Equal(color, Hsva.FromColor(color).ToColor());
        }
    }

    [Fact]
    public void A_hue_strip_and_square_choose_a_colour()
    {
        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0xFF), new Hsva(180, 1, 1, 1).ToColor());
        Assert.Equal(Color.FromRgb(0x80, 0x40, 0x40), new Hsva(0, 0.5, 0.50196, 1).ToColor());
        Assert.Equal(Color.FromArgb(0x80, 0xFF, 0x80, 0x00), new Hsva(30.1, 1, 1, 0.50196).ToColor());
        // Hues wrap: 360 is red again, and a negative hue counts back from it.
        Assert.Equal(Colors.Red, new Hsva(360, 1, 1, 1).ToColor());
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0xFF), new Hsva(-60, 1, 1, 1).ToColor());
    }

    [Theory]
    [InlineData("#F00", 0xFF, 0xFF, 0x00, 0x00)]
    [InlineData("#ff5a36", 0xFF, 0xFF, 0x5A, 0x36)]
    [InlineData("#FF5A3680", 0x80, 0xFF, 0x5A, 0x36)]
    [InlineData("  #000000  ", 0xFF, 0, 0, 0)]
    [InlineData("transparent", 0, 0, 0, 0)]
    public void Hex_text_reads_as_layout_yaml_writes_it(string text, byte a, byte r, byte g, byte b)
    {
        Assert.True(ColorText.TryParse(text, out var color));

        Assert.Equal(Color.FromArgb(a, r, g, b), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("FF0000")]
    [InlineData("#FF00")]
    [InlineData("#GG0000")]
    [InlineData("#FF00000")]
    [InlineData("red")]
    public void Text_that_isnt_a_colour_is_refused(string text) => Assert.False(ColorText.TryParse(text, out _));

    [Fact]
    public void A_colour_is_written_with_its_alpha_last_and_only_when_it_isnt_opaque()
    {
        Assert.Equal("#FF5A36", ColorText.Format(Color.FromRgb(0xFF, 0x5A, 0x36)));
        Assert.Equal("#FF5A3680", ColorText.Format(Color.FromArgb(0x80, 0xFF, 0x5A, 0x36)));
        Assert.Equal("#00000000", ColorText.Format(Color.FromArgb(0, 0, 0, 0)));
    }

    [Fact]
    public void Recent_colours_keep_the_latest_first_without_repeats()
    {
        var recent = new RecentColors(capacity: 3);

        recent.Add("#FF0000");
        recent.Add("#00FF00");
        recent.Add("#ff0000");
        recent.Add("#0000FF");
        recent.Add("#FFFFFF");
        recent.Add("not a colour");

        Assert.Equal(["#FFFFFF", "#0000FF", "#FF0000"], recent.Colors);
    }
}
