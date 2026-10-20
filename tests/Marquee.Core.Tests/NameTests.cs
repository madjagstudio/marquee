using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// `name` on widgets and background layers: kept for the theme editor's layers list, and ignored by the renderer.
public class NameTests
{
    const string Header = """
        canvas: { orientation: landscape }
        widgets:

        """;

    /// Line numbers in errors count from the top of the file; the header is 2 lines long.
    static ThemeException Error(string widgets) => Layouts.Error(Header + widgets);

    [Fact]
    public void A_name_is_kept_on_a_widget_a_group_child_and_a_layer()
    {
        var spec = Layouts.Parse("""
            canvas: { orientation: landscape }
            background:
              - { fill: "#000000", name: Night sky }
              - { image: panel.png }
            widgets:
              - { type: gauge, name: CPU dial, at: [20, 70], size: [340, 340], source: cpu.temp }
              - type: group
                name: Stats
                at: [0, 0]
                size: [100, 100]
                children:
                  - { type: text, name: CPU dial, at: [0, 0], size: [10, 10], text: hi }
                  - { type: value, at: [0, 0], size: [10, 10], source: x }
            """);

        Assert.Equal(new string?[] { "Night sky", null }, spec.Background.Select(l => l.Name));
        Assert.Equal("CPU dial", spec.Widgets[0].Name);
        var group = Assert.IsType<GroupWidget>(spec.Widgets[1]);
        Assert.Equal("Stats", group.Name);
        // Names needn't be unique: the child shares the gauge's.
        Assert.Equal(new string?[] { "CPU dial", null }, group.Children.Select(c => c.Name));
    }

    [Fact]
    public void A_name_takes_up_to_64_characters()
    {
        var longest = new string('n', NameParser.MaxLength);
        var spec = Layouts.Parse($$"""
            canvas: { orientation: landscape }
            background:
              - { fill: "#000000", name: {{longest}} }
            widgets:
              - { type: text, name: {{longest}}, at: [0, 0], size: [10, 10], text: hi }
            """);

        Assert.Equal(64, NameParser.MaxLength);
        Assert.Equal(longest, Assert.Single(spec.Background).Name);
        Assert.Equal(longest, Assert.Single(spec.Widgets).Name);
    }

    [Fact]
    public void A_name_over_64_characters_is_an_error_on_a_widget_and_a_layer()
    {
        var tooLong = new string('n', NameParser.MaxLength + 1);
        var widget = Error($"- type: text\n  at: [0, 0]\n  size: [10, 10]\n  text: hi\n  name: {tooLong}");
        var layer = Layouts.Error($"canvas: {{ orientation: landscape }}\nbackground:\n  - fill: \"#000000\"\n    name: {tooLong}");

        Assert.Equal((7, "name is too long (the limit is 64 characters)"), (widget.Line, widget.Reason));
        Assert.Equal((4, "name is too long (the limit is 64 characters)"), (layer.Line, layer.Reason));
    }

    /// Characters as people see them: an emoji with a skin tone, or a family joined by zero-width joiners, is one.
    [Theory]
    [InlineData("\U0001F44D\U0001F3FD")] // thumbs up, medium skin tone: 4 UTF-16 units
    [InlineData("\U0001F469\x200D\U0001F469\x200D\U0001F467")] // woman, woman, girl: 8 units
    [InlineData("e\x0301")] // e and a combining acute accent
    public void A_name_counts_what_reads_as_one_character_as_one(string character)
    {
        var longest = string.Concat(Enumerable.Repeat(character, NameParser.MaxLength));
        var spec = Layouts.Parse($$"""
            canvas: { orientation: landscape }
            widgets:
              - { type: text, name: "{{longest}}", at: [0, 0], size: [10, 10], text: hi }
            """);
        var tooLong = Error($"- type: text\n  at: [0, 0]\n  size: [10, 10]\n  text: hi\n  name: \"{longest}{character}\"");

        Assert.Equal(longest, Assert.Single(spec.Widgets).Name);
        Assert.Equal((7, "name is too long (the limit is 64 characters)"), (tooLong.Line, tooLong.Reason));
    }

    [Theory]
    [InlineData("name: \"\"", "name is empty")]
    [InlineData("name:", "name is empty")]
    [InlineData("name: [CPU, dial]", "name must be a single value, not a list or mapping")]
    [InlineData("name: { first: CPU }", "name must be a single value, not a list or mapping")]
    [InlineData("name: \"   \"", "name is only spaces")]
    [InlineData("name: \"\\t \\u00A0\"", "name is only spaces")]
    public void A_name_must_be_one_value_and_not_empty(string name, string reason)
    {
        var widget = Error($"- type: text\n  at: [0, 0]\n  size: [10, 10]\n  text: hi\n  {name}");
        var layer = Layouts.Error($"canvas: {{ orientation: landscape }}\nbackground:\n  - fill: \"#000000\"\n    {name}");

        Assert.Equal((7, reason), (widget.Line, widget.Reason));
        Assert.Equal((4, reason), (layer.Line, layer.Reason));
    }

    /// `marquee validate` reports a bad name and keeps checking the widgets and layers after it.
    [Fact]
    public void Validate_reports_a_bad_name_and_goes_on()
    {
        var tooLong = new string('n', NameParser.MaxLength + 1);
        var (spec, errors) = Layouts.Collect($$"""
            canvas: { orientation: landscape }
            background:
              - { fill: "#000000", name: {{tooLong}} }
              - { fill: "#101010", name: Shade }
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, name: "" }
              - { type: value, at: [0, 0], size: [10, 10], name: Load }
              - { type: text, at: [0, 0], size: [10, 10], text: hi, name: Kept }
            """);

        Assert.Equal(
            [
                "3: name is too long (the limit is 64 characters)",
                "6: name is empty",
                "7: value widget is missing 'source'",
            ],
            Layouts.Lines(errors));
        Assert.Equal("Shade", Assert.Single(spec.Background).Name);
        Assert.Equal("Kept", Assert.Single(spec.Widgets).Name);
    }

    /// The rotation snapshot's theme with a name on its layer, every widget, the group and each child: the renderer
    /// ignores names, so it draws the same approved image.
    [Fact]
    public void Names_change_nothing_on_screen()
    {
        using var frame = Frames.Render("""
            canvas: { orientation: landscape }
            styles:
              label: { size: 28, weight: semibold, color: "#E6ECF5", align: center }
            background:
              - { fill: "#0B0F1A", name: Backdrop }
            widgets:
              - { type: text, name: Tilted label, at: [60, 190], size: [360, 100], text: TILTED, rotate: -15, style: { size: 56, weight: bold, align: center, fill: "#FF5A36", radius: 16 } }
              - type: gauge
                name: GPU dial
                at: [480, 60]
                size: [360, 360]
                source: gpu.temp
                min: 20
                max: 100
                thickness: 30
                rotate: 90
                style: { fill: { linear: ["#36D1DC", "#FF5A36"] }, track: "#FFFFFF18", stroke: { cap: round } }
              - { type: bar, name: CPU bar, at: [900, 220], size: [320, 40], source: cpu.load, rotate: 45, style: { fill: "#A770EF", track: "#FFFFFF18", radius: 20 } }
              - type: group
                name: Panel
                at: [1320, 90]
                size: [480, 300]
                rotate: 20
                style: { fill: "#FFFFFF10", radius: 24, stroke: { width: 3, color: "#36D1DC" } }
                children:
                  - { type: text, name: Panel, at: [0, 30], size: [480, 50], text: GROUP, style: label }
                  - { type: bar, name: CPU bar, at: [40, 120], size: [400, 36], source: cpu.load, style: { fill: "#36D1DC", track: "#FFFFFF18", radius: 18 } }
                  - { type: text, name: Child, at: [40, 190], size: [400, 70], text: CHILD, rotate: -20, style: { size: 40, align: center, fill: "#FF5A3680", radius: 12 } }
            """, TestSensors.Of(("gpu.temp", 68), ("cpu.load", 62)));

        Snapshots.Match(frame, "overlays-rotate");
    }
}
