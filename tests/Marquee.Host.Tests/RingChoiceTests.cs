using Marquee.Core.Config;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using SkiaSharp;

namespace Marquee.Host.Tests;

/// What the LED ring shows for config.yaml's `ring:` and the theme on screen.
public class RingChoiceTests
{
    static readonly RingSpec Spinning = LayoutParser.Parse("""
        canvas: { orientation: landscape }
        ring:
          color: "#3A7BFF"
          effect: spin
          speed: 2
          brightness: 80
          source: cpu.temp
          when:
            - { above: 85, color: "#FF3A2E", effect: static }
        """).Ring!;

    [Fact]
    public void Theme_shows_the_themes_ring()
    {
        Assert.Same(Spinning, RingChoice.Resolve(RingMode.Theme, null, Spinning));
    }

    [Fact]
    public void Theme_shows_the_soft_white_default_for_a_theme_without_a_ring_block()
    {
        Assert.Same(RingSpec.Default, RingChoice.Resolve(RingMode.Theme, null, null));
    }

    [Fact]
    public void Off_shows_nothing_whatever_the_theme_says()
    {
        Assert.Null(RingChoice.Resolve(RingMode.Off, null, Spinning));
        Assert.Null(RingChoice.Resolve(RingMode.Off, null, null));
    }

    [Fact]
    public void A_color_is_that_color_static_at_full_brightness_for_every_theme()
    {
        var red = new SKColor(0xFF, 0x00, 0x00);

        foreach (var theme in new[] { Spinning, null })
        {
            var ring = RingChoice.Resolve(RingMode.Color, red, theme)!;

            Assert.Equal((red, RingEffect.Static, 1f, 1f), (ring.Color, ring.Effect, ring.Speed, ring.Brightness));
            Assert.Null(ring.Source);
            Assert.Empty(ring.When);
        }
    }

    [Fact]
    public void A_colors_alpha_dims_the_ring()
    {
        var halfBlue = new SKColor(0x00, 0x00, 0xFF, 0x80);

        var ring = RingChoice.Resolve(RingMode.Color, halfBlue, null)!;

        Assert.Equal(halfBlue, ring.Color); // RingRenderer scales by alpha: brightness 1 at alpha 0x80 is about half
    }
}
