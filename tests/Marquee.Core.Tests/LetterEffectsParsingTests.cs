using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Themes.Schema;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;
using static Marquee.Core.Tests.TestSupport.Validations;

namespace Marquee.Core.Tests;

/// The letter-effect style keys: `text_stroke`, `text_fill`, `italic`, `underline`, `strike` and `case`. Each is
/// optional, and each layers through `extends`, a widget's style and its `when` rules like any other style key.
public class LetterEffectsParsingTests
{
    static StyleSpec Style(string yaml) => StyleTable.Parse(Yaml.Load("{}")).Reference(Yaml.Load(yaml)!, "style");

    static ThemeException Error(string styles) => Assert.Throws<ThemeException>(() => StyleTable.Parse(Yaml.Load(styles)));

    [Fact]
    public void Every_letter_effect_parses()
    {
        var style = Style("""
            text_stroke: { width: 3, color: "#000000" }
            text_fill: { linear: ["#36D1DC", "#5B86E5"], angle: 90 }
            italic: true
            underline: true
            strike: false
            case: upper
            """);

        Assert.Equal(new TextStrokeSpec(3, new SolidPaint(SKColors.Black)), style.TextStroke);
        var fill = Assert.IsType<LinearGradientPaint>(style.TextFill);
        Assert.Equal([new SKColor(0x36, 0xD1, 0xDC), new SKColor(0x5B, 0x86, 0xE5)], fill.Colors);
        Assert.Equal(90f, fill.Angle);
        Assert.Equal(true, style.Italic);
        Assert.Equal(true, style.Underline);
        Assert.Equal(false, style.Strike);
        Assert.Equal(LetterCase.Upper, style.Case);
    }

    [Fact]
    public void Leaving_them_out_sets_none()
    {
        var style = Style("{ size: 20 }");

        Assert.Null(style.TextStroke);
        Assert.Null(style.TextFill);
        Assert.Null(style.Italic);
        Assert.Null(style.Underline);
        Assert.Null(style.Strike);
        Assert.Null(style.Case);
    }

    [Theory]
    [InlineData("upper", LetterCase.Upper)]
    [InlineData("lower", LetterCase.Lower)]
    [InlineData("title", LetterCase.Title)]
    [InlineData("none", LetterCase.None)]
    public void Case_takes_each_of_its_values(string value, LetterCase expected) => Assert.Equal(expected, Style($"case: {value}").Case);

    [Fact]
    public void A_text_stroke_takes_a_solid_color_or_a_gradient_and_each_key_alone()
    {
        Assert.Equal(new TextStrokeSpec(0, null), Style("text_stroke: { width: 0 }").TextStroke);
        Assert.Equal(new TextStrokeSpec(null, new SolidPaint(SKColors.Red)), Style("text_stroke: { color: \"#FF0000\" }").TextStroke);
        Assert.IsType<RadialGradientPaint>(Style("text_stroke: { color: { radial: [\"#FFF\", \"#000\"] } }").TextStroke!.Paint);
        Assert.Equal(new SolidPaint(SKColors.Lime), Style("text_fill: \"#00FF00\"").TextFill);
    }

    [Theory]
    [InlineData("base:\n  text_stroke: { width: -0.5, color: \"#000\" }", 2, "text_stroke.width must be at least 0")]
    [InlineData("base:\n  text_stroke: { width: thick }", 2, "text_stroke.width must be a number, not 'thick'")]
    [InlineData("base:\n  text_stroke: { color: black }", 2, "text_stroke.color: 'black' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"")]
    [InlineData("base:\n  text_stroke: { width: 2, cap: round }", 2, "Unknown key 'cap' in text_stroke. Expected one of: width, color")]
    [InlineData("base:\n  text_stroke: \"#000000\"", 2, "text_stroke must be a mapping of keys to values")]
    [InlineData("base:\n  text_fill: blue", 2, "text_fill: 'blue' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"")]
    [InlineData("base:\n  italic: yes", 2, "italic must be true or false, not 'yes'")]
    [InlineData("base:\n  underline: 1", 2, "underline must be true or false, not '1'")]
    [InlineData("base:\n  strike: [true]", 2, "strike must be a single value, not a list or mapping")]
    [InlineData("base:\n  case: capitals", 2, "case must be one of: upper, lower, title, none; not 'capitals'")]
    [InlineData("base:\n  case: Upper", 2, "case must be one of: upper, lower, title, none; not 'Upper'")]
    public void Letter_effect_errors(string styles, int line, string reason)
    {
        var ex = Error(styles);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Fact]
    public void Extends_merges_the_text_stroke_key_by_key()
    {
        var table = StyleTable.Parse(Yaml.Load("""
            outlined: { text_stroke: { width: 4, color: "#000000" }, italic: true, case: upper, text_fill: "#FF0000" }
            softer: { extends: outlined, text_stroke: { color: "#202020" }, case: none, underline: true }
            """));

        var softer = table.Reference(Yaml.Load("softer")!, "style");

        Assert.Equal(new TextStrokeSpec(4, new SolidPaint(new SKColor(0x20, 0x20, 0x20))), softer.TextStroke);
        Assert.Equal(new SolidPaint(SKColors.Red), softer.TextFill);
        Assert.Equal(true, softer.Italic);
        Assert.Equal(true, softer.Underline);
        Assert.Equal(LetterCase.None, softer.Case);
    }

    [Fact]
    public void Merge_prefers_the_upper_style_for_every_letter_effect()
    {
        var under = new StyleSpec
        {
            TextStroke = new TextStrokeSpec(2, new SolidPaint(SKColors.Black)), TextFill = new SolidPaint(SKColors.Red), Italic = true,
            Underline = true, Strike = true, Case = LetterCase.Upper,
        };
        var over = new StyleSpec { TextStroke = new TextStrokeSpec(Width: 5), Italic = false, Strike = false, Case = LetterCase.Title };

        var merged = StyleSpec.Merge(under, over);

        Assert.Equal(new TextStrokeSpec(5, new SolidPaint(SKColors.Black)), merged.TextStroke);
        Assert.Equal(new SolidPaint(SKColors.Red), merged.TextFill);
        Assert.Equal(false, merged.Italic);
        Assert.Equal(true, merged.Underline);
        Assert.Equal(false, merged.Strike);
        Assert.Equal(LetterCase.Title, merged.Case);
        Assert.Equal(under, StyleSpec.Merge(under, StyleSpec.Empty));
    }

    [Fact]
    public void A_when_rule_can_change_one_letter_effect_and_keep_the_rest()
    {
        var spec = Layouts.Parse("""
            canvas: { orientation: landscape }
            widgets:
              - type: value
                at: [0, 0]
                size: [100, 40]
                source: cpu.temp
                style: { text_stroke: { width: 3, color: "#000000" }, case: lower }
                when:
                  - { above: 80, style: { text_stroke: { color: "#FF0000" }, text_fill: "#FFFF00", strike: true } }
            """);
        var widget = spec.Widgets[0];
        var rule = widget.When[0];

        var hot = StyleSpec.Merge(widget.Style, rule.Style);

        Assert.Equal(new TextStrokeSpec(3, new SolidPaint(SKColors.Red)), hot.TextStroke);
        Assert.Equal(new SolidPaint(SKColors.Yellow), hot.TextFill);
        Assert.Equal(true, hot.Strike);
        Assert.Equal(LetterCase.Lower, hot.Case);
    }

    /// Every painter that draws text with a style takes the letter effects, and a group passes them down to its children.
    [Theory]
    [InlineData("text_stroke")]
    [InlineData("text_fill")]
    [InlineData("italic")]
    [InlineData("underline")]
    [InlineData("strike")]
    [InlineData("case")]
    public void The_catalog_says_which_widgets_draw_letter_effects(string key)
    {
        Assert.Equal(["text", "value", "gauge", "donut", "group"], ThemeSchema.Style.Key(key)!.AppliesTo);
        Assert.All(ThemeSchema.TextStroke.Keys, k => Assert.Equal(["text", "value", "gauge", "donut", "group"], k.AppliesTo));
    }

    [Fact]
    public void Bad_letter_effects_are_reported_with_their_lines_and_the_rest_still_checked()
    {
        using var folder = new TempTheme("""
            canvas: { orientation: landscape }
            styles:
              title: { text_stroke: { width: -1, color: "#000" } }
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { case: shout } }
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { italic: maybe } }
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { text_fill: "#00FF00", underline: true } }
            """);

        Assert.Equal(
            [
                "layout.yaml:3: error: text_stroke.width must be at least 0",
                "layout.yaml:5: error: case must be one of: upper, lower, title, none; not 'shout'",
                "layout.yaml:6: error: italic must be true or false, not 'maybe'",
            ],
            Lines(Validate(folder.Folder)));
    }
}
