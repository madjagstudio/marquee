using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class StyleParsingTests
{
    static StyleTable Table(string styles) => StyleTable.Parse(Yaml.Load(styles));

    static StyleSpec Reference(StyleTable table, string yaml) => table.Reference(Yaml.Load(yaml)!, "style");

    static ThemeException Error(string styles) => Assert.Throws<ThemeException>(() => Table(styles));

    [Fact]
    public void Every_property_parses()
    {
        var style = Reference(Table("{}"), """
            font: fonts/Orbitron.ttf
            size: 32
            weight: bold
            color: "#FFEEDD"
            fill: { linear: ["#000", "#333"], angle: 90 }
            track: "#FFFFFF20"
            stroke: { width: 2, color: "#FF0000", cap: round }
            shadow: { color: "#00000080", x: 2, y: 3, blur: 4 }
            glow: { color: "#FF0000", radius: 12 }
            pulse: { property: glow, period: 800, min: 0.2, max: 0.9 }
            opacity: 0.75
            radius: 8
            padding: 6
            align: center
            letter_spacing: 1.5
            line_height: 1.4
            wrap: true
            """);

        Assert.Equal("fonts/Orbitron.ttf", style.Font);
        Assert.Equal(32f, style.Size);
        Assert.Equal(700, style.Weight);
        Assert.Equal(new SKColor(0xFF, 0xEE, 0xDD), style.Color);
        Assert.IsType<LinearGradientPaint>(style.Fill);
        Assert.Equal(new SolidPaint(new SKColor(0xFF, 0xFF, 0xFF, 0x20)), style.Track);
        Assert.Equal(new StrokeSpec(2, new SolidPaint(SKColors.Red), SKStrokeCap.Round), style.Stroke);
        Assert.Equal(new ShadowSpec(new SKColor(0, 0, 0, 0x80), 2, 3, 4), style.Shadow);
        Assert.Equal(new GlowSpec(SKColors.Red, 12), style.Glow);
        Assert.Equal(new PulseSpec(PulseTarget.Glow, 800, 0.2f, 0.9f), style.Pulse);
        Assert.Equal(0.75f, style.Opacity);
        Assert.Equal(8f, style.Radius);
        Assert.Equal(6f, style.Padding);
        Assert.Equal(TextAlign.Center, style.Align);
        Assert.Equal(1.5f, style.LetterSpacing);
        Assert.Equal(1.4f, style.LineHeight);
        Assert.Equal(true, style.Wrap);
    }

    [Theory]
    [InlineData("light", 300)]
    [InlineData("normal", 400)]
    [InlineData("semibold", 600)]
    [InlineData("800", 800)]
    public void Weight_takes_names_or_numbers(string weight, int expected) =>
        Assert.Equal(expected, Reference(Table("{}"), $"weight: {weight}").Weight);

    [Theory]
    [InlineData("letter_spacing: -20", -20f, null)]
    [InlineData("letter_spacing: 100", 100f, null)]
    [InlineData("line_height: 0.5", null, 0.5f)]
    [InlineData("line_height: 4", null, 4f)]
    public void Spacing_takes_the_ends_of_its_ranges(string yaml, float? letterSpacing, float? lineHeight)
    {
        var style = Reference(Table("{}"), yaml);

        Assert.Equal(letterSpacing, style.LetterSpacing);
        Assert.Equal(lineHeight, style.LineHeight);
    }

    [Fact]
    public void Extends_layers_a_style_over_its_parent()
    {
        var table = Table("""
            base:
              font: Inter
              size: 32
              color: "#FFFFFF"
              stroke: { width: 2, color: "#FF0000" }
            hot:
              extends: base
              color: "#FF4040"
              stroke: { color: "#FFFF00" }
            """);

        var hot = Reference(table, "hot");

        Assert.Equal("Inter", hot.Font);
        Assert.Equal(32f, hot.Size);
        Assert.Equal(new SKColor(0xFF, 0x40, 0x40), hot.Color);
        Assert.Equal(new StrokeSpec(2, new SolidPaint(SKColors.Yellow)), hot.Stroke);
    }

    [Fact]
    public void Extends_chains_resolve_in_any_order()
    {
        var table = Table("""
            hottest: { extends: hot, size: 48 }
            hot: { extends: base, color: "#FF0000" }
            base: { size: 24, align: right }
            """);

        var hottest = Reference(table, "hottest");

        Assert.Equal(48f, hottest.Size);
        Assert.Equal(SKColors.Red, hottest.Color);
        Assert.Equal(TextAlign.Right, hottest.Align);
    }

    [Fact]
    public void Inline_styles_can_extend_named_ones()
    {
        var style = Reference(Table("base: { size: 24, color: \"#FFFFFF\" }"), "{ extends: base, size: 40 }");

        Assert.Equal(40f, style.Size);
        Assert.Equal(SKColors.White, style.Color);
    }

    [Fact]
    public void Merge_prefers_the_upper_style_and_merges_nested_fields()
    {
        var under = new StyleSpec { Size = 20, Color = SKColors.White, Pulse = new PulseSpec(PulseTarget.Opacity, 1000) };
        var over = new StyleSpec { Color = SKColors.Red, Pulse = new PulseSpec(Min: 0.1f) };

        var merged = StyleSpec.Merge(under, over);

        Assert.Equal(20f, merged.Size);
        Assert.Equal(SKColors.Red, merged.Color);
        Assert.Equal(new PulseSpec(PulseTarget.Opacity, 1000, 0.1f), merged.Pulse);
    }

    [Fact]
    public void Inherited_keeps_only_text_properties()
    {
        var group = new StyleSpec
        {
            Font = "Inter", Size = 30, Weight = 700, Color = SKColors.Red, Align = TextAlign.Center, LetterSpacing = 2, LineHeight = 1.5f,
            Wrap = true, Fill = new SolidPaint(SKColors.Blue), Radius = 8, Opacity = 0.5f, Padding = 4,
        };

        Assert.Equal(
            new StyleSpec
            {
                Font = "Inter", Size = 30, Weight = 700, Color = SKColors.Red, Align = TextAlign.Center,
                LetterSpacing = 2, LineHeight = 1.5f, Wrap = true,
            },
            group.Inherited());
    }

    [Fact]
    public void A_cycle_names_every_style_in_it()
    {
        var ex = Error("""
            a: { extends: b }
            b: { extends: c }
            c: { extends: a }
            """);

        Assert.Equal("Style 'a' extends itself: a → b → c → a", ex.Reason);
        Assert.Equal(1, ex.Line);
    }

    [Fact]
    public void A_style_cannot_extend_itself()
    {
        var ex = Error("""
            ok: { size: 3 }
            loop:
              extends: loop
            """);

        Assert.Equal("Style 'loop' extends itself: loop → loop", ex.Reason);
        Assert.Equal(2, ex.Line);
    }

    [Fact]
    public void Extending_an_unknown_style_points_at_the_extends_line()
    {
        var ex = Error("""
            base: { size: 3 }
            hot:
              color: "#FF0000"
              extends: bsae
            """);

        Assert.Equal("Unknown style 'bsae'. Defined styles: base, hot", ex.Reason);
        Assert.Equal(4, ex.Line);
    }

    [Fact]
    public void Referencing_an_unknown_style_lists_the_defined_ones()
    {
        Assert.Equal("Unknown style 'hot'. Defined styles: base",
            Assert.Throws<ThemeException>(() => Reference(Table("base: { size: 3 }"), "hot")).Reason);
        Assert.Equal("Unknown style 'hot': no styles are defined",
            Assert.Throws<ThemeException>(() => Reference(Table("{}"), "hot")).Reason);
    }

    [Theory]
    [InlineData("base: { colour: \"#FFF\" }", 1, "Unknown key 'colour' in style 'base'. Expected one of: extends, font, size, weight, color, fill, track, stroke, shadow, glow, pulse, opacity, radius, padding, align, blend, letter_spacing, line_height, wrap, text_stroke, text_fill, italic, underline, strike, case")]
    [InlineData("base:\n  wrap: yes", 2, "wrap must be true or false, not 'yes'")]
    [InlineData("base:\n  letter_spacing: -20.5", 2, "letter_spacing must be between -20 and 100")]
    [InlineData("base:\n  letter_spacing: 100.5", 2, "letter_spacing must be between -20 and 100")]
    [InlineData("base:\n  line_height: 0.49", 2, "line_height must be between 0.5 and 4")]
    [InlineData("base:\n  line_height: 4.01", 2, "line_height must be between 0.5 and 4")]
    [InlineData("base:\n  line_height: tall", 2, "line_height must be a number, not 'tall'")]
    [InlineData("base: { opacity: 1.5 }", 1, "opacity must be between 0 and 1")]
    [InlineData("base: { weight: heavy }", 1, "weight must be a number, not 'heavy'")]
    [InlineData("base: { weight: 950 }", 1, "weight must be between 100 and 900")]
    [InlineData("base:\n  pulse: { property: size }", 2, "pulse.property must be one of: opacity, glow; not 'size'")]
    [InlineData("base:\n  pulse: { period: 10 }", 2, "pulse.period must be at least 50")]
    [InlineData("base:\n  stroke: { cap: pointy }", 2, "stroke.cap must be one of: butt, round, square; not 'pointy'")]
    [InlineData("base:\n  glow: { radius: -1 }", 2, "glow.radius must be at least 0")]
    [InlineData("base: big", 1, "style 'base' must be a mapping of keys to values")]
    [InlineData("- base", 1, "styles must be a mapping of style names to styles")]
    public void Style_errors(string styles, int line, string reason)
    {
        var ex = Error(styles);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Fact]
    public void Layout_reports_style_errors_even_when_no_widget_uses_the_style()
    {
        var ex = Layouts.Error("""
            canvas: { orientation: landscape }
            styles:
              a: { extends: a }
            """);

        Assert.Equal("Style 'a' extends itself: a → a", ex.Reason);
        Assert.Equal(3, ex.Line);
    }
}
