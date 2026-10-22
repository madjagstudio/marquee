using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

public class LayeredPropertiesTests
{
    const string Layout = """
        canvas: { orientation: landscape }
        background: [{ fill: "#102030" }]
        styles: { meter: { color: "#FFFFFF" } }
        ring: { source: cpu.temp, when: [{ above: 80, color: "#FF0000" }] }
        widgets:
          - type: gauge
            name: Temperature dial
            at: [0, 0]
            size: [200, 200]
            source: cpu.temp
            style: meter
            when: [{ above: 80, style: { color: "#FF0000" } }]
          - { type: value, at: [0, 200], size: [100, 40], source: cpu.temp }
        """;

    [Fact]
    public void Gauge_basics_and_more_styling_follow_the_catalog_and_spec_order()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        var model = PropertyModel.Build(session.Document, [session.Widgets[0]]);

        Assert.Equal(new[] { "size", "source", "min", "max", "start", "sweep", "thickness", "needle", "style.color" }.Order(),
            model.Basics.Select(row => row.Name).Order());
        // Warnings retain their existing dedicated rule editor rather than becoming ordinary value rows.
        Assert.Single(model.Rules);
        Assert.Equal(new[] { PlainSection.Text, PlainSection.FillAndBorder, PlainSection.Effects, PlainSection.Position, PlainSection.Motion },
            model.MoreStyling.Select(group => group.Section));
        Assert.Equal(new[] { "Text", "Fill and border", "Effects", "Position", "Motion" },
            model.MoreStyling.Select(group => group.Title));
        Assert.All(model.MoreStyling, group => Assert.NotEmpty(group.Rows));
    }

    [Theory]
    [InlineData("gauge", "gauge")]
    [InlineData("value", "value")]
    [InlineData("layer", "layer.fill")]
    [InlineData("canvas", "canvas")]
    [InlineData("style", "style")]
    [InlineData("rule", "when")]
    [InlineData("ring", "ring")]
    [InlineData("ring-rule", "ring.when")]
    public void Every_target_partitions_raw_rows_once_and_labels_every_nested_row(string target, string owner)
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        var model = Target(session, target);
        CheckPartition(model);
        foreach (var row in Walk(model.Raw))
        {
            var words = Assert.IsType<PlainLabel>(PlainLabels.For(owner, row.Name));
            Assert.Equal(words.Label, row.PlainLabel);
            Assert.Equal(words.Tooltip, row.Tooltip);
        }
    }

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    [InlineData("example-agents")]
    [InlineData("example-showcase")]
    public void Example_widgets_including_nested_lists_remain_reachable(string example)
    {
        using var theme = TempTheme.Example(example);
        using var session = theme.Open();
        foreach (var widget in Widgets(session.Widgets))
        {
            var model = PropertyModel.Build(session.Document, [widget]);
            CheckPartition(model);
            foreach (var row in Walk(model.Raw))
            {
                var words = Assert.IsType<PlainLabel>(PlainLabels.For(widget.Type, row.Name));
                Assert.Equal(words.Label, row.PlainLabel);
                Assert.Equal(words.Tooltip, row.Tooltip);
            }
        }
    }

    [Theory]
    [InlineData("gauge", "Temperature dial")]
    [InlineData("value", "Value: CPU temperature")]
    [InlineData("layer", "Background: Fill")]
    [InlineData("style", "Style: meter")]
    [InlineData("canvas", "Theme settings")]
    [InlineData("rule", "Warning")]
    [InlineData("ring", "Ring")]
    [InlineData("ring-rule", "Ring warning")]
    public void Headings_name_the_target_in_plain_words(string target, string heading)
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        Assert.Equal(heading, Target(session, target).Heading);
    }

    [Theory]
    [InlineData("source", "gpu.temp")]
    [InlineData("style.color", "\"#12AB34\"")]
    public void Setting_a_basic_writes_the_same_yaml_as_raw(string name, string value)
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        var model = Target(session, "gauge");
        var basic = model.Basics.Single(row => row.Name == name);
        var raw = model.Raw.Single(row => row.Name == name);
        Assert.Same(raw, basic);
        session.Apply(model.Set(basic, YamlValue.Parse(value)));
        var expected = session.Document.Text;
        Assert.True(session.Undo());
        session.Apply(model.Set(raw, YamlValue.Parse(value)));
        Assert.Equal(expected, session.Document.Text);
        Assert.Equal(YamlValue.Parse(value), session.Document.ValueAt(raw.Paths.Single()));
    }

    [Fact]
    public void Mixed_and_empty_widget_selections_are_partitioned_without_empty_groups()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        var mixed = PropertyModel.Build(session.Document, session.Widgets);
        CheckPartition(mixed);
        Assert.Equal("2 widgets", mixed.Heading);
        Assert.Equal("What it shows", mixed.Raw.Single(row => row.Name == "source").PlainLabel);
        var empty = PropertyModel.Build(session.Document, []);
        CheckPartition(empty);
        Assert.Empty(empty.MoreStyling);
        Assert.Equal("Theme settings", empty.Heading);
    }

    [Fact]
    public void Uncatalogued_rows_fall_back_to_the_key_and_schema_help()
    {
        var key = ThemeSchema.Canvas.Key("fps")!;
        var row = new PropertyRow("unknown.fps", key);
        Assert.Equal(key.Name, row.PlainLabel);
        Assert.Equal(key.Help, row.Tooltip);
    }

    static PropertyModel Target(EditorSession session, string target) => target switch
    {
        "gauge" => PropertyModel.Build(session.Document, [session.Widgets[0]]),
        "value" => PropertyModel.Build(session.Document, [session.Widgets[1]]),
        "layer" => PropertyModel.ForLayer(session.Document, YamlPath.Parse("background[0]")),
        "canvas" => PropertyModel.ForCanvas(session.Document),
        "style" => PropertyModel.ForStyle(session.Document, "meter"),
        "rule" => PropertyModel.ForRule(session.Document, YamlPath.Parse("widgets[0].when[0]")),
        "ring-rule" => PropertyModel.ForRule(session.Document, YamlPath.Parse("ring.when[0]")),
        "ring" => PropertyModel.ForRing(session.Document),
        _ => throw new ArgumentException(target),
    };

    static void CheckPartition(PropertyModel model)
    {
        Assert.Equal(model.Rows.Concat(model.Style.SelectMany(section => section.Rows)), model.Raw);
        var plain = model.Basics.Concat(model.MoreStyling.SelectMany(group => group.Rows)).ToList();
        Assert.Equal(model.Raw.Count, plain.Count);
        Assert.Equal(plain.Count, plain.Select(row => row.Name).Distinct().Count());
        foreach (var row in model.Raw) Assert.Same(row, Assert.Single(plain, candidate => candidate.Name == row.Name));
        Assert.Equal(Walk(model.Raw).Select(row => row.Name).Order(), Walk(plain).Select(row => row.Name).Order());
        Assert.All(model.MoreStyling, group => Assert.NotEmpty(group.Rows));
    }

    static IEnumerable<PropertyRow> Walk(IEnumerable<PropertyRow> rows) =>
        rows.SelectMany(row => new[] { row }.Concat(Walk(row.Children)).Concat(Walk(row.Items.SelectMany(item => item.Rows))));

    static IEnumerable<WidgetRef> Widgets(IEnumerable<WidgetRef> widgets) =>
        widgets.SelectMany(widget => new[] { widget }.Concat(Widgets(widget.Children)));
}
