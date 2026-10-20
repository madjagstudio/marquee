using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;
using static Marquee.Core.Tests.TestSupport.Validations;

namespace Marquee.Core.Tests;

/// `rotate`, `hidden` and `locked` on widgets, `hidden` and `locked` on background layers, and the `blend` style key.
public class OverlayParsingTests
{
    const string Header = """
        canvas: { orientation: landscape }
        styles:
          glass: { blend: screen }
          frosted: { extends: glass, opacity: 0.5 }
        widgets:

        """;

    static IReadOnlyList<WidgetSpec> Widgets(string widgets) => Layouts.Parse(Header + widgets).Widgets;

    /// Line numbers in errors count from the top of the file; the header is 5 lines long.
    static ThemeException Error(string widgets) => Layouts.Error(Header + widgets);

    [Fact]
    public void Rotate_hidden_and_locked_parse_on_any_widget_and_group()
    {
        var widgets = Widgets("""
            - { type: text, at: [0, 0], size: [10, 10], text: hi, rotate: 45, hidden: true, locked: true }
            - { type: bar, at: [0, 0], size: [10, 10], source: x, rotate: -360 }
            - type: group
              at: [0, 0]
              size: [100, 100]
              rotate: 360
              hidden: false
              locked: true
              children:
                - { type: gauge, at: [0, 0], size: [10, 10], source: x, rotate: 12.5, hidden: true }
            """);

        Assert.Equal((45f, true, true), (widgets[0].Rotate, widgets[0].Hidden, widgets[0].Locked));
        Assert.Equal((-360f, false, false), (widgets[1].Rotate, widgets[1].Hidden, widgets[1].Locked));
        var group = Assert.IsType<GroupWidget>(widgets[2]);
        Assert.Equal((360f, false, true), (group.Rotate, group.Hidden, group.Locked));
        Assert.Equal((12.5f, true, false), (group.Children[0].Rotate, group.Children[0].Hidden, group.Children[0].Locked));
    }

    [Fact]
    public void A_widget_is_upright_shown_and_unlocked_by_default()
    {
        var widget = Assert.Single(Widgets("- { type: value, at: [0, 0], size: [10, 10], source: x }"));

        Assert.Equal((0f, false, false), (widget.Rotate, widget.Hidden, widget.Locked));
        Assert.Null(widget.Style.Blend);
    }

    [Theory]
    [InlineData("normal", SKBlendMode.SrcOver)]
    [InlineData("multiply", SKBlendMode.Multiply)]
    [InlineData("screen", SKBlendMode.Screen)]
    [InlineData("overlay", SKBlendMode.Overlay)]
    [InlineData("darken", SKBlendMode.Darken)]
    [InlineData("lighten", SKBlendMode.Lighten)]
    [InlineData("add", SKBlendMode.Plus)]
    public void Blend_takes_the_background_layers_modes(string name, SKBlendMode mode)
    {
        var widget = Assert.Single(Widgets($"- {{ type: text, at: [0, 0], size: [10, 10], text: hi, style: {{ blend: {name} }} }}"));

        Assert.Equal(mode, widget.Style.Blend);
    }

    [Fact]
    public void Blend_comes_from_named_styles_extends_and_when_rules()
    {
        var widgets = Widgets("""
            - { type: text, at: [0, 0], size: [10, 10], text: hi, style: glass }
            - { type: text, at: [0, 0], size: [10, 10], text: hi, style: frosted }
            - type: value
              at: [0, 0]
              size: [10, 10]
              source: x
              style: glass
              when:
                - { above: 80, style: { blend: multiply } }
            """);

        Assert.Equal(SKBlendMode.Screen, widgets[0].Style.Blend);
        Assert.Equal((SKBlendMode.Screen, 0.5f), (widgets[1].Style.Blend!.Value, widgets[1].Style.Opacity!.Value));
        Assert.Equal(SKBlendMode.Multiply, Assert.Single(widgets[2].When).Style.Blend);
        Assert.Equal(SKBlendMode.Multiply, StyleSpec.Merge(widgets[2].Style, widgets[2].When[0].Style).Blend);
        Assert.Equal(SKBlendMode.Screen, StyleSpec.Merge(widgets[2].Style, StyleSpec.Empty).Blend);
    }

    /// Blend belongs to a widget's own layer, like its box, so a group doesn't pass it to its children.
    [Fact]
    public void A_group_does_not_pass_its_blend_down() =>
        Assert.Null(new StyleSpec { Blend = SKBlendMode.Screen, Color = SKColors.Red }.Inherited().Blend);

    [Fact]
    public void Background_layers_take_hidden_and_locked()
    {
        var layers = Layouts.Parse("""
            canvas: { orientation: landscape }
            background:
              - fill: "#101018"
                hidden: true
                locked: true
              - image: sky.png
                locked: true
              - { shader: a.sksl, hidden: true }
              - { video: a.mp4 }
            """).Background;

        Assert.Equal((true, true), (layers[0].Hidden, layers[0].Locked));
        Assert.Equal((false, true), (layers[1].Hidden, layers[1].Locked));
        Assert.Equal((true, false), (layers[2].Hidden, layers[2].Locked));
        Assert.Equal((false, false), (layers[3].Hidden, layers[3].Locked));
    }

    [Theory]
    [InlineData("- { type: text, at: [0, 0], size: [10, 10], text: hi, rotate: 360.5 }", 6, "rotate must be between -360 and 360")]
    [InlineData("- { type: text, at: [0, 0], size: [10, 10], text: hi, rotate: -361 }", 6, "rotate must be between -360 and 360")]
    [InlineData("- type: text\n  at: [0, 0]\n  size: [10, 10]\n  text: hi\n  rotate: left", 10, "rotate must be a number, not 'left'")]
    [InlineData("- type: group\n  at: [0, 0]\n  size: [10, 10]\n  rotate: 400\n  children: []", 9, "rotate must be between -360 and 360")]
    [InlineData("- type: text\n  at: [0, 0]\n  size: [10, 10]\n  text: hi\n  hidden: yes", 10, "hidden must be true or false, not 'yes'")]
    [InlineData("- type: text\n  at: [0, 0]\n  size: [10, 10]\n  text: hi\n  locked: 1", 10, "locked must be true or false, not '1'")]
    [InlineData("- type: text\n  at: [0, 0]\n  size: [10, 10]\n  text: hi\n  hidden: [true]", 10, "hidden must be a single value, not a list or mapping")]
    [InlineData("- type: text\n  at: [0, 0]\n  size: [10, 10]\n  text: hi\n  style:\n    blend: burn", 11, "blend must be one of: normal, multiply, screen, overlay, darken, lighten, add; not 'burn'")]
    [InlineData("- type: value\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  when:\n    - { above: 1, style: { blend: dodge } }", 11, "blend must be one of: normal, multiply, screen, overlay, darken, lighten, add; not 'dodge'")]
    public void Overlay_errors(string widgets, int line, string reason)
    {
        var ex = Error(widgets);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Theory]
    [InlineData("styles:\n  soft:\n    blend: Screen", 4, "blend must be one of: normal, multiply, screen, overlay, darken, lighten, add; not 'Screen'")]
    [InlineData("background:\n  - fill: \"#000\"\n    hidden: maybe", 4, "hidden must be true or false, not 'maybe'")]
    [InlineData("background:\n  - fill: \"#000\"\n    locked: no", 4, "locked must be true or false, not 'no'")]
    public void Style_and_layer_errors(string section, int line, string reason)
    {
        var ex = Layouts.Error("canvas: { orientation: landscape }\n" + section);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    /// `marquee validate` checks hidden widgets and layers like any others, so unhiding one can't break the theme.
    [Fact]
    public void Validate_still_checks_what_is_hidden()
    {
        var (_, errors) = Layouts.Collect("""
            canvas: { orientation: landscape }
            background:
              - { fill: red, hidden: true }
            widgets:
              - { type: value, at: [0, 0], size: [10, 10], hidden: true }
              - type: group
                at: [0, 0]
                size: [10, 10]
                hidden: true
                children:
                  - { type: bar, at: [0, 0], size: [10, 10], source: x, max: -1 }
            """);

        Assert.Equal(
            [
                "3: fill: 'red' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"",
                "5: value widget is missing 'source'",
                "11: bar widget max must be greater than min",
            ],
            Layouts.Lines(errors));
    }

    /// Hidden layers and widgets load nothing for `marquee run`: a hidden clip doesn't decode, and a hidden image's file
    /// isn't read. `marquee validate` still loads them, so a missing file is reported before anyone unhides it.
    [Fact]
    public void Hidden_assets_are_loaded_by_validate_but_not_by_run()
    {
        using var folder = new TempTheme("""
            canvas: { orientation: landscape }
            background:
              - { image: gone-layer.png, hidden: true }
            widgets:
              - { type: image, at: [0, 0], size: [10, 10], src: gone-widget.png, hidden: true }
              - type: group
                at: [0, 0]
                size: [10, 10]
                hidden: true
                children:
                  - { type: image, at: [0, 0], size: [10, 10], src: gone-child.png }
            """);

        using (var theme = folder.Load())
            Assert.Empty(theme.Warnings);

        Assert.Equal(
            [
                "gone-child.png:0: error: Image 'gone-child.png' was not found and was skipped.",
                "gone-layer.png:0: error: Image 'gone-layer.png' was not found and was skipped.",
                "gone-widget.png:0: error: Image 'gone-widget.png' was not found and was skipped.",
            ],
            Lines(Validate(folder.Folder)));
    }
}
