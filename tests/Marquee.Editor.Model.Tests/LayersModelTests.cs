using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// The layers panel's tree: widgets front to back (the top of the list is the YAML's last widget, drawn last), groups
/// with their children, and the background layers in a section of their own; and where a dragged layer lands.
public class LayersModelTests
{
    [Theory]
    [InlineData("Install now", "Text · Install now", "Text: Install now")]
    [InlineData("{{cpu.temp}}", "Text · {{cpu.temp}}", "Text: {{cpu.temp}}")]
    [InlineData("{cpu.temp:0} °C", "Text · {CPU temperature:0} °C", "Text: {CPU temperature:0} °C")]
    public void Text_names_expand_readings_but_preserve_literal_words(string text, string layerName, string heading)
    {
        using var folder = TempTheme.With($"canvas: {{ orientation: landscape }}\nwidgets:\n  - {{ type: text, at: [0, 0], size: [200, 30], text: '{text}' }}\n");
        using var session = folder.Open();
        Assert.Equal(layerName, Assert.Single(LayersModel.Of(session).Widgets).Name);
        session.Select([P("widgets[0]")]);
        Assert.Equal(heading, PropertyModel.For(session).Heading);
    }

    [Fact]
    public void Agent_bar_names_and_property_headings_use_the_plain_sensor_name()
    {
        using var folder = TempTheme.With("canvas: { orientation: landscape }\nwidgets:\n  - { type: bar, at: [0, 0], size: [200, 30], source: claude.seven_day.pct }\n");
        using var session = folder.Open();
        Assert.Equal("Bar · Claude Code: weekly limit used", Assert.Single(LayersModel.Of(session).Widgets).Name);
        session.Select([P("widgets[0]")]);
        Assert.Equal("Bar: Claude Code: weekly limit used", PropertyModel.For(session).Heading);
    }

    static YamlPath P(string path) => YamlPath.Parse(path);

    [Fact]
    public void The_tree_runs_front_to_back_with_the_background_below()
    {
        using var folder = TempTheme.Example("example-landscape");
        using var session = folder.Open();

        var layers = LayersModel.Of(session);

        Assert.Equal(10, layers.Widgets.Count);
        Assert.Equal(P("widgets[9]"), layers.Widgets[0].Path);
        Assert.Equal("Value · CPU usage", layers.Widgets[0].Name);
        Assert.False(layers.Widgets[0].IsNamed);
        var group = layers.Widgets[^1];
        Assert.Equal(P("widgets[0]"), group.Path);
        Assert.Equal(LayerKind.Group, group.Kind);
        Assert.Equal(
            [P("widgets[0].children[3]"), P("widgets[0].children[2]"), P("widgets[0].children[1]"), P("widgets[0].children[0]")],
            group.Children.Select(c => c.Path));
        Assert.Equal("Gauge · CPU temperature", group.Children[2].Name);

        Assert.Equal([P("background[1]"), P("background[0]")], layers.Background.Select(b => b.Path));
        Assert.All(layers.Background, b => Assert.Equal(LayerKind.Background, b.Kind));
        Assert.Equal(["Fill · radial", "Fill · linear"], layers.Background.Select(b => b.Name));
        Assert.Same(group.Children[2], layers.Find(P("widgets[0].children[1]")));
    }

    [Fact]
    public void Names_hidden_and_locked_come_from_the_file()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "background:\n" +
            "  - { fill: \"#000000\", name: Night, locked: true }\n" +
            "  - { fill: \"#FF0000\", hidden: true }\n" +
            "widgets:\n" +
            "  - { type: text, at: [0, 0], size: [100, 20], text: Hi, name: Greeting, hidden: true, locked: true }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();

        var layers = LayersModel.Of(session);

        var widget = Assert.Single(layers.Widgets);
        Assert.Equal(("Greeting", true, true, true), (widget.Name, widget.IsNamed, widget.Hidden, widget.Locked));
        Assert.Equal(("Fill · #FF0000", true, false), (layers.Background[0].Name, layers.Background[0].Hidden,
            layers.Background[0].Locked));
        Assert.Equal(("Night", true, false, true), (layers.Background[1].Name, layers.Background[1].IsNamed,
            layers.Background[1].Hidden, layers.Background[1].Locked));
    }

    [Fact]
    public void A_dropped_layer_lands_in_front_of_behind_or_inside_its_target()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - { type: text, at: [0, 0], size: [100, 20], text: A }\n" +
            "  - { type: text, at: [0, 30], size: [100, 20], text: B }\n" +
            "  - type: group\n" +
            "    at: [400, 100]\n" +
            "    size: [300, 200]\n" +
            "    children:\n" +
            "      - { type: text, at: [0, 0], size: [100, 20], text: C }\n" +
            "  - { type: text, at: [0, 60], size: [100, 20], text: D }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();
        var layers = LayersModel.Of(session);

        // In front of D: the top. Behind B: the item before it, counted without A.
        Assert.Equal(new MoveLayer(P("widgets[0]"), null, 3), layers.Drop(P("widgets[0]"), P("widgets[3]"), DropPlace.Above));
        Assert.Equal(new MoveLayer(P("widgets[3]"), null, 1), layers.Drop(P("widgets[3]"), P("widgets[1]"), DropPlace.Below));
        Assert.Equal(new MoveLayer(P("widgets[0]"), P("widgets[2]"), 1), layers.Drop(P("widgets[0]"), P("widgets[2]"), DropPlace.Into));
        Assert.Equal(new MoveLayer(P("widgets[3]"), P("widgets[2]"), 0),
            layers.Drop(P("widgets[3]"), P("widgets[2].children[0]"), DropPlace.Below));
        Assert.Equal(new MoveLayer(P("widgets[2].children[0]"), null, 4),
            layers.Drop(P("widgets[2].children[0]"), P("widgets[3]"), DropPlace.Above));

        // Where it already is, into itself, or into what isn't a group: nowhere.
        Assert.Null(layers.Drop(P("widgets[0]"), P("widgets[1]"), DropPlace.Below));
        Assert.Null(layers.Drop(P("widgets[2]"), P("widgets[2].children[0]"), DropPlace.Above));
        Assert.Null(layers.Drop(P("widgets[0]"), P("widgets[1]"), DropPlace.Into));
    }
}
