using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// The widgets as the layers panel and the canvas see them: a tree in YAML order (back to front), each widget with its
/// path, type, name, box on the canvas, and hidden, locked and editable state, matched to the parsed widget by walking
/// `widgets` and each group's `children` in order, as the parser keeps them.
public class WidgetRefTests
{
    public static TheoryData<string> Examples => new(TempTheme.Examples);

    static IEnumerable<WidgetRef> All(IEnumerable<WidgetRef> widgets) => widgets.SelectMany(w => All(w.Children).Prepend(w));

    static YamlPath P(string path) => YamlPath.Parse(path);

    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_widget_of_each_example_is_matched_to_its_place_in_the_file(string name)
    {
        using var folder = TempTheme.Example(name);
        using var session = folder.Open();
        var document = session.Document;

        foreach (var widget in All(session.Widgets))
        {
            Assert.True(document.Exists(widget.Path), $"{widget.Path} isn't in the file");
            Assert.Equal(new YamlText(widget.Type), document.ValueAt(widget.Path.Child("type")));
            Assert.Equal(widget.Spec.Bounds, widget.LocalBounds);
            Assert.Equal(widget.Spec.Hidden, widget.Hidden);
            Assert.Equal(widget.Spec.Locked, widget.Locked);
            Assert.Equal(widget.Spec.Name ?? widget.Name, widget.Name);
            Assert.Equal(document.IsEditable(widget.Path), widget.IsEditable);
            Assert.Same(widget, session.Widget(widget.Path));
            Assert.Equal(widget.Type == "group", widget.Spec is GroupWidget);
            var children = widget.Type == "group" ? widget.Path.Child("children") : null;
            Assert.Equal(widget.Children.Select((_, i) => children!.Item(i)), widget.Children.Select(child => child.Path));
            if (children is not null) Assert.False(document.Exists(children.Item(widget.Children.Count)));
        }

        Assert.Equal(session.Widgets.Select((_, i) => P("widgets").Item(i)), session.Widgets.Select(w => w.Path));
        Assert.False(document.Exists(P("widgets").Item(session.Widgets.Count)));
        var parsed = LayoutParserCount(session.Document.Text);
        Assert.Equal(parsed, All(session.Widgets).Count());
    }

    static int LayoutParserCount(string text)
    {
        static int Count(IEnumerable<WidgetSpec> widgets) => widgets.Sum(w => 1 + (w is GroupWidget g ? Count(g.Children) : 0));
        return Count(Marquee.Core.Themes.Parsing.LayoutParser.Parse(text).Widgets);
    }

    [Fact]
    public void A_childs_box_on_the_canvas_adds_its_groups_corner()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();

        var cpu = session.Widget(P("widgets[1]"))!;
        var glow = cpu.Children[0];

        Assert.Equal(("group", "CPU"), (cpu.Type, cpu.Name));
        Assert.Equal(SKRect.Create(340, 24, 360, 432), cpu.Bounds);
        Assert.Equal(SKRect.Create(20, 40, 320, 320), glow.LocalBounds);
        Assert.Equal(SKRect.Create(360, 64, 320, 320), glow.Bounds);
        Assert.Equal(new SKPoint(340, 24), glow.Offset);
    }

    [Fact]
    public void The_showcases_names_hidden_widget_and_spans_are_read()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var all = All(session.Widgets).ToList();

        var radar = session.Widget(P("widgets[0].children[3]"))!;
        Assert.Equal(("video", "Radar"), (radar.Type, radar.Name));
        var temperature = all.Single(w => w.Name == "CPU temperature");
        Assert.Equal("value", temperature.Type);
        Assert.IsType<ValueWidget>(temperature.Spec);
        var hidden = Assert.Single(all, w => w.Hidden);
        Assert.Equal("Shape · rect", hidden.Name);
        Assert.Contains(all, w => w.Name == "Bar · gpu.load" && w.Spec.Name is null);
    }

    [Fact]
    public void A_text_widget_with_spans_is_a_text_widget_and_names_are_made_up_by_type()
    {
        using var folder = TempTheme.With(
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - { type: text, at: [0, 0], size: [100, 40], spans: [{ text: \"{cpu.temp:0}\" }, { text: \"°C\" }] }\n" +
            "  - { type: text, at: [0, 0], size: [100, 40], text: \"Load\n    is   high and climbing past every limit we set\" }\n" +
            "  - { type: gauge, at: [0, 0], size: [100, 100], source: cpu.temp, locked: true }\n" +
            "  - { type: image, at: [0, 0], size: [10, 10], src: images/art.png, hidden: true }\n" +
            "  - { type: graph, at: [0, 0], size: [100, 40], series: [{ source: gpu.load }, { source: cpu.load }] }\n" +
            "  - { type: donut, at: [0, 0], size: [100, 100], slices: [{ source: ram.used_pct }, { source: 100 - ram.used_pct }] }\n" +
            "  - { type: group, at: [0, 0], size: [10, 10], children: [] }\n" +
            "  - { type: value, at: [0, 0], size: [10, 10], source: \"cpu.clock / 1000\", name: Clock }\n");
        using var session = folder.Open();
        var widgets = session.Widgets;
        Assert.Null(session.LoadError);

        Assert.IsType<SpansWidget>(widgets[0].Spec);
        Assert.Equal(
        [
            "Text · {cpu.temp:0}°C",
            "Text · Load is high and climbing past…",
            "Gauge · cpu.temp",
            "Image · images/art.png",
            "Graph · gpu.load",
            "Donut · ram.used_pct",
            "Group",
            "Clock",
        ], widgets.Select(w => w.Name));
        Assert.Equal(["text", "text", "gauge", "image", "graph", "donut", "group", "value"], widgets.Select(w => w.Type));
        Assert.True(widgets[2].Locked);
        Assert.True(widgets[3].Hidden);
        Assert.Empty(widgets[6].Children);
    }

    [Fact]
    public void Widgets_reached_through_an_alias_are_listed_and_not_editable()
    {
        using var folder = TempTheme.With(
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - &tile { type: shape, shape: rect, at: [0, 0], size: [10, 10] }\n" +
            "  - *tile\n" +
            "  - { type: shape, shape: ellipse, at: [20, 0], size: [10, 10] }\n");
        using var session = folder.Open();

        Assert.Equal([false, false, true], session.Widgets.Select(w => w.IsEditable));
        Assert.Equal(P("widgets[2]"), session.Widgets[2].Path);
        Assert.Equal(SKRect.Create(20, 0, 10, 10), session.Widgets[2].Bounds);
    }
}
