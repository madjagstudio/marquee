using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// The properties panel is built from the catalog alone: for every widget in every example, the rows are the type's
/// keys and the style keys it draws with, no more and no fewer, and nothing the file sets is left without a row.
public class PropertyCatalogTests
{
    public static TheoryData<string> Examples => new(TempTheme.Examples);

    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_widget_shows_its_types_keys_and_no_others(string example)
    {
        using var folder = TempTheme.Example(example);
        using var session = folder.Open();

        foreach (var widget in All(session.Widgets))
        {
            var model = PropertyModel.Build(session.Document, [widget]);
            var expected = ExpectedWidgetKeys(session.Document, widget);

            Assert.Equal(expected, model.Rows.Select(row => row.Name).ToList());
            foreach (var row in model.Rows)
            {
                var key = ThemeSchema.WidgetKeys(widget.Type).Single(key => key.Name == row.Name);
                Assert.Equal((key.Kind, key.Unit, key.Min, key.Max, key.Help), (row.Kind, row.Unit, row.Key.Min, row.Max, row.Help));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_widget_shows_the_style_keys_its_type_draws_with(string example)
    {
        using var folder = TempTheme.Example(example);
        using var session = folder.Open();

        foreach (var widget in All(session.Widgets))
        {
            var model = PropertyModel.Build(session.Document, [widget]);
            var shown = model.Style.SelectMany(section => section.Rows).ToList();

            var expected = ThemeSchema.Style.Keys
                .Where(key => key.Name != "extends" && Applies(key, widget.Type))
                .Select(key => "style." + key.Name)
                .Order(StringComparer.Ordinal);
            Assert.Equal(expected, shown.Select(row => row.Name).Order(StringComparer.Ordinal));

            // A block's rows are its keys the type draws with: a gauge's stroke is its cap alone.
            foreach (var block in shown.Where(row => row.Kind == SchemaKind.Block))
            {
                var keys = ThemeSchema.Block(block.Key.Of!)!.Keys.Where(key => Applies(key, widget.Type)).Select(key => $"{block.Name}.{key.Name}");
                Assert.Equal(keys, block.Children.Select(row => row.Name));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Nothing_the_file_sets_on_a_widget_is_left_without_a_row(string example)
    {
        using var folder = TempTheme.Example(example);
        using var session = folder.Open();

        foreach (var widget in All(session.Widgets))
        {
            var model = PropertyModel.Build(session.Document, [widget]);
            var map = (YamlMap)session.Document.ValueAt(widget.Path)!;
            foreach (var (key, value) in map.Entries)
            {
                switch (key)
                {
                    case "style":
                        // The style section has a row for every key written in place; a name is the style dropdown.
                        if (value is YamlMap style)
                            foreach (var styleKey in style.Entries.Select(e => e.Key).Where(k => k != "extends"))
                                Assert.NotNull(model.Row("style." + styleKey));
                        else
                            Assert.Equal(((YamlText)value).Value, model.StyleUse!.Name);
                        break;
                    case "when":
                        Assert.Equal(((YamlList)value).Items.Count, model.Rules.Count);
                        break;
                    case "children":
                        break;
                    default:
                        Assert.True(model.Row(key) is not null, $"{widget.Path}.{key} has no row");
                        break;
                }
            }
        }
    }

    [Fact]
    public void Each_row_carries_the_catalogs_kind_unit_range_and_help()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var dial = session.Widgets.SelectMany(w => w.Children).Single(w => w.Name == "CPU dial");

        var model = PropertyModel.Build(session.Document, [dial]);

        var sweep = model.Row("sweep")!;
        Assert.Equal(SchemaKind.Number, sweep.Kind);
        Assert.Equal(SchemaUnit.Degrees, sweep.Unit);
        Assert.Equal(-ThemeSchema.Widget("gauge")!.Key("sweep")!.Max, sweep.Min);
        Assert.Equal(1, sweep.Step);
        Assert.Equal(ThemeSchema.Widget("gauge")!.Key("sweep")!.Default, sweep.Default);
        Assert.False(sweep.IsSet);
        Assert.Null(sweep.Value);
        Assert.Equal(ThemeSchema.Widget("gauge")!.Key("sweep")!.Help, sweep.Help);

        var thickness = model.Row("thickness")!;
        Assert.True(thickness.IsSet);
        Assert.Equal(new YamlText("12"), thickness.Value);
        Assert.True(thickness.IsEditable);

        Assert.False(model.Row("type")!.IsEditable);
        Assert.Equal(["horizontal", "vertical"], PropertyModel.Build(session.Document,
            [session.Widgets.SelectMany(w => w.Children).First(w => w.Type == "bar")]).Row("direction")!.Choices);
    }

    [Fact]
    public void Blocks_are_sub_sections_with_their_own_rows()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var dial = session.Widgets.SelectMany(w => w.Children).Single(w => w.Name == "CPU dial");

        var model = PropertyModel.Build(session.Document, [dial]);

        var needle = model.Row("needle")!;
        Assert.Equal(SchemaKind.Block, needle.Kind);
        Assert.True(needle.IsSet);
        Assert.Equal(["needle.width", "needle.length", "needle.color", "needle.cap"], needle.Children.Select(r => r.Name));
        Assert.Equal(new YamlText("0.68"), model.Row("needle.length")!.Value);
        Assert.Equal(dial.Path.Child("needle").Child("length"), model.Row("needle.length")!.Paths.Single());
        Assert.Equal(new YamlText("{0:0}"), model.Row("ticks.labels")!.Value);
        Assert.True(model.Row("ticks.labels")!.Key.TakesFalse);
    }

    [Fact]
    public void Lists_are_items_each_with_their_own_rows()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var chart = session.Widgets.SelectMany(w => w.Children).Single(w => w.Name == "Load chart");

        var model = PropertyModel.Build(session.Document, [chart]);

        var series = model.Row("series")!;
        Assert.Equal(SchemaKind.List, series.Kind);
        Assert.Equal(2, series.Items.Count);
        Assert.Equal(chart.Path.Child("series").Item(1), series.Items[1].Path);
        Assert.Equal(new YamlText("cpu.load"), model.Row("series[1].source")!.Value);
        Assert.Equal(SchemaKind.SensorKey, model.Row("series[1].source")!.Kind);
        // A series' style takes the style keys a graph draws with, written in place in the item.
        var style = model.Row("series[1].style")!;
        Assert.Equal(SchemaKind.Style, style.Kind);
        Assert.Equal(new YamlText("#36D1DC"), model.Row("series[1].style.stroke.color")!.Value);
        Assert.Contains(style.Children, row => row.Name == "series[1].style.fill");
        Assert.DoesNotContain(style.Children, row => row.Name == "series[1].style.font");
        Assert.True(series.CanAdd);
        Assert.True(series.CanRemove);
    }

    [Fact]
    public void A_spans_style_offers_only_the_keys_a_span_may_set()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var temperature = session.Widgets.SelectMany(w => w.Children).Single(w => w.Name == "CPU temperature");

        var model = PropertyModel.Build(session.Document, [temperature]);

        var spanKeys = ThemeSchema.TextSpan.Key("style")!.StyleKeys.Where(k => k != "extends").Select(k => "spans[1].style." + k);
        Assert.Equal(spanKeys, model.Row("spans[1].style")!.Children.Select(row => row.Name));
        Assert.Equal(new YamlText("24"), model.Row("spans[1].style.size")!.Value);
    }

    [Fact]
    public void Only_the_keys_for_a_shapes_form_are_shown()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var shapes = All(session.Widgets).Where(w => w.Type == "shape").ToList();
        var arc = shapes.Single(w => session.Document.TryGetScalar(w.Path.Child("shape"), out var s) && s == "arc");
        var rect = shapes.First(w => session.Document.TryGetScalar(w.Path.Child("shape"), out var s) && s == "rect");

        Assert.NotNull(PropertyModel.Build(session.Document, [arc]).Row("sweep"));
        Assert.Null(PropertyModel.Build(session.Document, [rect]).Row("sweep"));
        Assert.Null(PropertyModel.Build(session.Document, [rect]).Row("start"));
    }

    [Fact]
    public void Only_one_of_a_values_format_and_spans_is_shown()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var values = All(session.Widgets).Where(w => w.Type == "value").ToList();
        var withSpans = values.Single(w => w.Name == "CPU temperature");
        var withFormat = values.First(w => w.Name != "CPU temperature");

        Assert.NotNull(PropertyModel.Build(session.Document, [withSpans]).Row("spans"));
        Assert.Null(PropertyModel.Build(session.Document, [withSpans]).Row("format"));
        Assert.NotNull(PropertyModel.Build(session.Document, [withFormat]).Row("format"));
        Assert.Null(PropertyModel.Build(session.Document, [withFormat]).Row("spans"));
    }

    [Fact]
    public void The_style_section_is_grouped_as_the_panel_shows_it()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var title = All(session.Widgets).Single(w => w.Name == "Title");
        var bar = All(session.Widgets).First(w => w.Type == "bar");

        var text = PropertyModel.Build(session.Document, [title]).Style;
        Assert.Equal(["Text", "Colour and fill", "Outline", "Effects"], text.Select(s => s.Title));
        Assert.Equal(["style.font", "style.size", "style.weight", "style.color", "style.text_fill", "style.align",
            "style.letter_spacing", "style.line_height", "style.wrap", "style.padding", "style.italic", "style.underline",
            "style.strike", "style.case", "style.text_stroke"], text[0].Rows.Select(r => r.Name));
        Assert.Equal(["style.shadow", "style.glow", "style.opacity", "style.radius", "style.pulse", "style.blend"],
            text[3].Rows.Select(r => r.Name));

        // A bar draws no text, so its color, the filled part's, goes with fill and track.
        var meter = PropertyModel.Build(session.Document, [bar]).Style;
        Assert.Equal(["Colour and fill", "Outline", "Effects"], meter.Select(s => s.Title));
        Assert.Equal(["style.color", "style.fill", "style.track"], meter[0].Rows.Select(r => r.Name));
    }

    [Fact]
    public void Every_style_key_has_a_section()
    {
        foreach (var key in ThemeSchema.Style.Keys.Where(k => k.Name != "extends"))
            Assert.NotEqual(StyleSections.More, StyleSections.Of(key.Name, drawsText: true));
        Assert.Equal(StyleSections.More, StyleSections.Of("a_key_added_later", drawsText: true));
    }

    [Fact]
    public void Defaults_are_the_types_own()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var chart = All(session.Widgets).Single(w => w.Name == "Load chart");
        var light = All(session.Widgets).Single(w => w.Name == "CPU status light");
        var dial = All(session.Widgets).Single(w => w.Name == "CPU dial");

        var graph = PropertyModel.Build(session.Document, [chart]);
        Assert.Equal(StyleDefaults.GraphStrokeWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            graph.Row("style.stroke.width")!.Default);
        Assert.Equal("round", graph.Row("style.stroke.cap")!.Default);
        Assert.Equal(ThemeSchema.Widget("graph")!.Key("points")!.Default, graph.Row("points")!.Default);

        var shape = PropertyModel.Build(session.Document, [light]);
        Assert.Equal(ThemeSchema.Stroke.Key("width")!.Default, shape.Row("style.stroke.width")!.Default);
        Assert.Equal("butt", shape.Row("style.stroke.cap")!.Default);

        Assert.Equal(ThemeSchema.Widget("gauge")!.Key("start")!.Default, PropertyModel.Build(session.Document, [dial]).Row("start")!.Default);
        Assert.Equal("#FFFFFF", PropertyModel.Build(session.Document, [dial]).Row("style.color")!.Default);
    }

    /// The keys a widget's rows should be, from the catalog: the type's keys in the catalog's order, less `style` (the
    /// Style section), `when` (the rules) and `children` (the layers panel), less a key of another form of the
    /// widget (OnlyFor), and of each group of keys that exclude each other, only the one the file sets (or the first).
    static List<string> ExpectedWidgetKeys(LayoutDocument document, WidgetRef widget)
    {
        var variant = ThemeSchema.Widget(widget.Type)!;
        var keys = ThemeSchema.WidgetKeys(widget.Type)
            .Where(key => key.Kind != SchemaKind.Style && key.Of is not ("when" or "widget"))
            .Where(key => key.OnlyFor is not { } only
                || (document.TryGetScalar(widget.Path.Child(only.Key), out var value) && value == only.Value))
            .Select(key => key.Name)
            .ToList();
        foreach (var group in new[] { variant.ExactlyOneOf, variant.AtMostOneOf }.Where(g => g.Count > 0))
        {
            var chosen = group.FirstOrDefault(name => document.Exists(widget.Path.Child(name))) ?? group[0];
            keys.RemoveAll(name => group.Contains(name) && name != chosen);
        }

        return keys;
    }

    static bool Applies(SchemaKey key, string type) => key.AppliesTo.Count == 0 || key.AppliesTo.Contains(type);

    static IEnumerable<WidgetRef> All(IEnumerable<WidgetRef> widgets) =>
        widgets.SelectMany(widget => All(widget.Children).Prepend(widget));
}
