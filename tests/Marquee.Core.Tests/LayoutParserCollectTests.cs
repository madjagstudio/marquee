using System.Runtime.CompilerServices;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// The collecting parse behind `marquee validate`: every independent problem in one run, by line, without repeats.
public class LayoutParserCollectTests
{
    const string WidgetTypes = "widget type must be one of: text, value, bar, gauge, graph, image, video, shape, donut, group";

    static string SampleLayout(string name, [CallerFilePath] string file = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "themes", name, "layout.yaml"));

    [Fact]
    public void Reports_every_independent_error_in_document_order()
    {
        var (spec, errors) = Layouts.Collect("""
            canvas: { orientation: sideways }
            styles:
              good: { size: 20 }
              bad: { size: huge }
            background:
              - fill: "#000000"
              - fill: "#GG0000"
              - image: a.png
                blend: sideways
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: bad }
              - { type: value, at: [0, 0], size: [10, 10] }
              - type: group
                at: [0, 0]
                size: [100, 100]
                children:
                  - { type: text, at: [0, 0], size: [10, 10], text: ok }
                  - { type: bar, at: [0, nope], size: [10, 10], source: cpu.load }
                  - { type: image, at: [0, 0], size: [10, 10], src: a.png, fit: sideways }
              - { type: text, at: [0, 0], size: [10, 10], text: bye, smooth: -1 }
            """);

        Assert.Equal(
            [
                "1: canvas.orientation must be one of: portrait, landscape; not 'sideways'",
                "4: size must be a number, not 'huge'",
                "7: fill: '#GG0000' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"",
                "9: blend must be one of: normal, multiply, screen, overlay, darken, lighten, add; not 'sideways'",
                "12: value widget is missing 'source'",
                "18: at must be a number, not 'nope'",
                "19: fit must be one of: cover, contain, stretch, none; not 'sideways'",
                "20: smooth must be between 0 and 10000",
            ],
            Layouts.Lines(errors));

        // The partial spec keeps what was valid: the good layer, the widget whose style was bad, the group's good child.
        Assert.Single(spec.Background);
        Assert.Equal(2, spec.Widgets.Count);
        Assert.Single(Assert.IsType<GroupWidget>(spec.Widgets[1]).Children);
    }

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    [InlineData("example-agents")]
    public void A_valid_layout_reports_nothing_and_builds_the_same_spec_as_Parse(string theme)
    {
        var yaml = SampleLayout(theme);

        var (spec, errors) = Layouts.Collect(yaml);

        Assert.Empty(errors);
        Structural.Equal(Layouts.Parse(yaml), spec);
    }

    [Theory]
    [InlineData("")]
    [InlineData("- a list")]
    [InlineData("canvas: landscape")]
    [InlineData("background: []")]
    [InlineData("canvas: { orientation: landscape }\nwidget: []")]
    [InlineData("canvas:\n  orientation: landscape\n  orientation: portrait")]
    [InlineData("background: [1, 2\ncanvas: { orientation: landscape }")]
    [InlineData("canvas: { orientation: landscape }\nstyles: [a]")]
    [InlineData("canvas: { orientation: landscape }\nstyles: { a: { extends: b } }")]
    [InlineData("canvas: { orientation: landscape }\nstyles:\n  a: { extends: b }\n  b: { extends: a }")]
    [InlineData("canvas: { orientation: landscape }\nbackground: { fill: \"#000000\" }")]
    [InlineData("canvas: { orientation: landscape }\nwidgets:\n  - type: group\n    at: [0, 0]\n    size: [1, 1]")]
    [InlineData("canvas: { orientation: landscape }\nstyles: { a: { size: 2 } }\nwidgets:\n  - { type: text, at: [0, 0], size: [1, 1], text: a, style: b }")]
    public void A_layout_with_one_problem_reports_exactly_the_error_Parse_throws(string yaml)
    {
        var thrown = Layouts.Error(yaml);

        var (_, errors) = Layouts.Collect(yaml);

        Assert.Equal(Layouts.Lines([thrown]), Layouts.Lines(errors));
    }

    [Fact]
    public void A_syntax_error_is_the_only_error()
    {
        var (spec, errors) = Layouts.Collect("""
            canvas:
              orientation: sideways
             fps: 30
            widgets:
              - { type: nope }
            """);

        var error = Assert.Single(errors);
        Assert.StartsWith("YAML syntax error:", error.Reason);
        Assert.Equal(3, error.Line);
        Assert.Empty(spec.Widgets);
    }

    [Fact]
    public void An_unknown_top_level_key_does_not_stop_the_known_sections()
    {
        var (_, errors) = Layouts.Collect("""
            canvas: { orientation: landscape }
            widget: []
            widgets:
              - { type: nope }
            """);

        Assert.Equal(
            [
                "2: Unknown key 'widget' in layout.yaml. Expected one of: canvas, styles, background, widgets, ring",
                $"4: {WidgetTypes}; not 'nope'",
            ],
            Layouts.Lines(errors));
    }

    [Fact]
    public void A_missing_canvas_still_lets_the_rest_be_checked()
    {
        var (spec, errors) = Layouts.Collect("""
            widgets:
              - { type: nope }
            """);

        Assert.Equal(["1: layout.yaml is missing 'canvas'", $"2: {WidgetTypes}; not 'nope'"], Layouts.Lines(errors));
        Assert.Equal(new CanvasSpec(ThemeOrientation.Landscape, CanvasSpec.DefaultFps), spec.Canvas);
    }

    [Fact]
    public void A_bad_style_is_reported_once_however_often_it_is_used()
    {
        var (spec, errors) = Layouts.Collect("""
            canvas: { orientation: landscape }
            styles:
              bad: { size: huge }
              child: { extends: bad, color: "#FFFFFF" }
            widgets:
              - type: value
                at: [0, 0]
                size: [10, 10]
                source: cpu.temp
                style: bad
                when:
                  - { above: 80, style: bad }
              - { type: text, at: [0, 0], size: [10, 10], text: a, style: { extends: bad } }
              - { type: text, at: [0, 0], size: [10, 10], text: b, style: child }
            """);

        Assert.Equal(["3: size must be a number, not 'huge'"], Layouts.Lines(errors));
        Assert.Equal(3, spec.Widgets.Count);
    }

    [Fact]
    public void A_style_cycle_is_reported_once()
    {
        var (_, errors) = Layouts.Collect("""
            canvas: { orientation: landscape }
            styles:
              a: { extends: b }
              b: { extends: a }
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: x, style: b }
            """);

        Assert.Equal(["3: Style 'a' extends itself: a → b → a"], Layouts.Lines(errors));
    }

    [Fact]
    public void Styles_that_are_not_a_mapping_do_not_make_every_reference_unknown()
    {
        var (_, errors) = Layouts.Collect("""
            canvas: { orientation: landscape }
            styles: [label]
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: x, style: label }
            """);

        Assert.Equal(["2: styles must be a mapping of style names to styles"], Layouts.Lines(errors));
    }

    [Fact]
    public void A_broken_group_still_has_its_children_checked()
    {
        var (_, errors) = Layouts.Collect("""
            canvas: { orientation: landscape }
            widgets:
              - type: group
                at: nope
                size: [10, 10]
                children:
                  - { type: nope }
            """);

        Assert.Equal(["4: at must be a pair of numbers like [10, 20]", $"7: {WidgetTypes}; not 'nope'"], Layouts.Lines(errors));
    }

    [Fact]
    public void An_aliased_mistake_is_reported_once()
    {
        var (_, errors) = Layouts.Collect("""
            canvas: { orientation: landscape }
            widgets:
              - &broken { type: group, at: [0, 0], size: [10, 10], children: [{ type: nope }] }
              - *broken
              - *broken
            """);

        Assert.Equal([$"3: {WidgetTypes}; not 'nope'"], Layouts.Lines(errors));
    }

    [Fact]
    public void Collecting_stops_at_the_widget_limit()
    {
        var widgets = string.Concat(Enumerable.Repeat("  - { type: text, at: [0, 0], size: [1, 1], text: a }\n", 2001));

        var (_, errors) = Layouts.Collect("canvas: { orientation: landscape }\nwidgets:\n" + widgets + "  - { type: nope }\n");

        Assert.Equal(["2003: The layout has more than 2000 widgets"], Layouts.Lines(errors));
    }

    [Fact]
    public void Background_limits_are_reported_once()
    {
        var shaders = string.Concat(Enumerable.Repeat("  - shader: a.sksl\n", 6));
        var fills = string.Concat(Enumerable.Repeat("  - fill: \"#000000\"\n", 17));

        Assert.Equal(["7: a background can have at most 4 shader layers"],
            Layouts.Lines(Layouts.Collect("canvas: { orientation: landscape }\nbackground:\n" + shaders).Errors));
        Assert.Equal(["19: a background can have at most 16 layers"],
            Layouts.Lines(Layouts.Collect("canvas: { orientation: landscape }\nbackground:\n" + fills).Errors));
    }

    [Fact]
    public void At_most_200_errors_are_kept_and_the_rest_are_counted()
    {
        var widgets = string.Concat(Enumerable.Repeat("  - { type: nope }\n", 205));

        var (_, errors) = Layouts.Collect("canvas: { orientation: landscape }\nwidgets:\n" + widgets);

        Assert.Equal(201, errors.Count);
        Assert.Equal(Enumerable.Range(3, 200), errors.Take(200).Select(e => e.Line));
        Assert.Equal("…and 5 more errors", errors[200].Reason);
        Assert.Equal(0, errors[200].Line);
    }

    [Fact]
    public void Parse_still_throws_the_error_it_always_threw_first()
    {
        // Parse checks canvas before widgets, wherever they are in the file; collecting sorts by line instead.
        const string yaml = """
            widgets:
              - { type: nope }
            canvas: { orientation: sideways }
            """;

        var thrown = Layouts.Error(yaml);

        Assert.Equal("3: canvas.orientation must be one of: portrait, landscape; not 'sideways'", Layouts.Lines([thrown])[0]);
        Assert.Equal([2, 3], Layouts.Collect(yaml).Errors.Select(e => e.Line));
    }
}
