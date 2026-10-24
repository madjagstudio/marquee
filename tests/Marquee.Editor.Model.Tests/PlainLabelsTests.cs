using Marquee.Core.Themes.Schema;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

public class PlainLabelsTests
{
    [Fact]
    public void Every_numeric_key_has_an_authored_slider_range_inside_its_schema_range()
    {
        var count = 0;
        foreach (var (owner, path) in PlainLabels.AllKeys())
        {
            var key = SchemaKeyAt(owner, path);
            if (key.Kind is not (SchemaKind.Number or SchemaKind.Integer or SchemaKind.Size or SchemaKind.Point or SchemaKind.FontWeight)
                && key.ItemKind != SchemaKind.Number) continue;
            count++;
            var label = PlainLabels.For(owner, path)!;
            var min = label.SliderMin;
            var max = label.SliderMax;
            Assert.True(min is not null && max is not null && min < max, $"Missing range: {owner}/{path}");
            Assert.True(min >= (key.Min ?? double.NegativeInfinity), $"Low end: {owner}/{path}");
            Assert.True(max <= (key.Max ?? double.PositiveInfinity), $"High end: {owner}/{path}");
            var schemaWidth = (key.Max ?? double.PositiveInfinity) - (key.Min ?? double.NegativeInfinity);
            if (schemaWidth > 200)
                Assert.True(max - min < schemaWidth, $"Range must be narrower: {owner}/{path}");
            if (label.Minimum is { } floor)
                Assert.True(floor >= (key.Min ?? double.NegativeInfinity) && floor <= min, $"Minimum: {owner}/{path}");
        }
        Assert.True(count > 100); // Includes nested styles, paints, rules and list items.
    }

    [Theory]
    [InlineData("text", "style.size")]
    [InlineData("style", "style.size")]
    [InlineData("when", "style.size")]
    [InlineData("text", "spans[0].style.size")]
    public void Text_has_a_readable_ui_floor_even_in_nested_styles(string owner, string path) =>
        Assert.Equal(6, PlainLabels.For(owner, path)!.Minimum);

    static SchemaKey SchemaKeyAt(string owner, string path)
    {
        IEnumerable<SchemaKey> keys = ThemeSchema.Widgets.Names.Contains(owner) ? ThemeSchema.WidgetKeys(owner)
            : owner.StartsWith("layer.", StringComparison.Ordinal) ? ThemeSchema.Background.KeysOf(ThemeSchema.Block(owner)!)
            : ThemeSchema.Block(owner)!.Keys;
        if (owner == "canvas") path = path["canvas.".Length..];
        if (owner == "style" && path.StartsWith("style.", StringComparison.Ordinal)) path = path["style.".Length..];
        SchemaKey? result = null;
        foreach (var part in path.Split('.'))
        {
            result = keys.Single(key => key.Name == part.Split('[')[0]);
            keys = result.Kind == SchemaKind.Style ? ThemeSchema.Style.Keys
                : result.Kind == SchemaKind.Paint ? ThemeSchema.Gradient.Variants.SelectMany(ThemeSchema.Gradient.KeysOf).DistinctBy(key => key.Name)
                : result.Of is { } of && ThemeSchema.Block(of) is { } block ? block.Keys
                : [];
        }
        return result!;
    }

    [Fact]
    public void Actual_property_rows_use_paths_the_catalog_recognises()
    {
        foreach (var example in TempTheme.Examples)
        {
            using var folder = TempTheme.Example(example);
            using var session = folder.Open();
            Check("canvas", PropertyModel.ForCanvas(session.Document));
            Check("ring", PropertyModel.ForRing(session.Document));
            foreach (var widget in Widgets(session.Widgets))
                Check(widget.Type, PropertyModel.Build(session.Document, [widget]));
        }

        static IEnumerable<WidgetRef> Widgets(IEnumerable<WidgetRef> widgets) =>
            widgets.SelectMany(widget => Widgets(widget.Children).Prepend(widget));

        static IEnumerable<PropertyRow> Rows(IEnumerable<PropertyRow> rows) =>
            rows.SelectMany(row => Rows(row.Children.Concat(row.Items.SelectMany(item => item.Rows))).Prepend(row));

        static void Check(string owner, PropertyModel model)
        {
            foreach (var row in Rows(model.Rows.Concat(model.Style.SelectMany(section => section.Rows))))
                Assert.True(PlainLabels.For(owner, row.Name) is not null, $"Unlabelled property row: {owner}/{row.Name}");
        }
    }

    [Fact]
    public void Every_schema_key_has_a_plain_label_tooltip_and_reachable_section()
    {
        var keys = PlainLabels.AllKeys().ToList();
        Assert.NotEmpty(keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var (owner, path) in keys)
        {
            var label = PlainLabels.For(owner, path);
            Assert.True(label is not null, $"Missing plain label: {owner}/{path}");
            Assert.False(string.IsNullOrWhiteSpace(label.Label));
            Assert.False(string.IsNullOrWhiteSpace(label.Tooltip));
            Assert.EndsWith(".", label.Tooltip);
            Assert.True(Enum.IsDefined(label.Section), $"Unreachable section: {owner}/{path}");
            if (label.Section is PlainSection.Basics or PlainSection.WhatItShows)
                Assert.Contains(owner, label.BasicFor);
        }
    }

    [Fact]
    public void Labels_are_unique_within_each_owner()
    {
        foreach (var owner in PlainLabels.AllKeys().GroupBy(key => key.Owner))
        {
            var duplicates = owner.GroupBy(key => PlainLabels.For(key.Owner, key.KeyPath)?.Label, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => $"{owner.Key}: {group.Key}: {string.Join(", ", group.Select(key => key.KeyPath))}");
            Assert.Empty(duplicates);
        }
    }

    [Fact]
    public void Walk_includes_all_roots_and_nested_blocks_lists_styles_and_paints()
    {
        var keys = PlainLabels.AllKeys().ToHashSet();
        foreach (var widget in ThemeSchema.Widgets.Names)
            foreach (var key in ThemeSchema.WidgetKeys(widget))
                Assert.Contains((widget, key.Name), keys);
        foreach (var layer in ThemeSchema.Background.Variants)
            foreach (var key in ThemeSchema.Background.KeysOf(layer))
                Assert.Contains((layer.Id, key.Name), keys);
        foreach (var block in new[] { ThemeSchema.Canvas, ThemeSchema.Style, ThemeSchema.When, ThemeSchema.Ring, ThemeSchema.RingWhen })
            foreach (var key in block.Keys)
            {
                var prefix = block.Id == "canvas" ? "canvas." : block.Id == "style" && key.Name != "extends" ? "style." : "";
                Assert.Contains((block.Id, prefix + key.Name), keys);
            }
        Assert.Contains(("gauge", "needle.width"), keys);
        Assert.Contains(("gauge", "ticks.labels"), keys);
        Assert.Contains(("graph", "series[0].style.stroke.width"), keys);
        Assert.Contains(("donut", "slices[0].source"), keys);
        Assert.Contains(("text", "spans[0].style.text_stroke.color.radial"), keys);
        Assert.Contains(("shape", "style.fill.radius"), keys);
        Assert.Contains(("ring", "when[0].brightness"), keys);
    }

    [Theory]
    [InlineData("gauge", "source", "What it shows")]
    [InlineData("gauge", "style.stroke", "Border")]
    [InlineData("layer.fill", "opacity", "Transparency")]
    [InlineData("value", "style.blend", "Blend with what's behind")]
    [InlineData("shape", "rotate", "Rotation")]
    [InlineData("bar", "smooth", "Smooth movement")]
    [InlineData("text", "style.text_stroke", "Letter outline")]
    public void Required_plain_words_are_used(string owner, string path, string expected) =>
        Assert.Equal(expected, PlainLabels.For(owner, path)?.Label);

    [Theory]
    [InlineData("gauge", "source")]
    [InlineData("gauge", "needle")]
    [InlineData("gauge", "start")]
    [InlineData("gauge", "sweep")]
    [InlineData("gauge", "style.color")]
    [InlineData("gauge", "size")]
    [InlineData("gauge", "when")]
    [InlineData("shape", "shape")]
    [InlineData("shape", "style.fill")]
    [InlineData("donut", "hole")]
    [InlineData("text", "text")]
    [InlineData("value", "format")]
    [InlineData("graph", "kind")]
    [InlineData("image", "src")]
    [InlineData("layer.fill", "fill")]
    [InlineData("style", "style.font")]
    [InlineData("when", "above")]
    [InlineData("when", "style")]
    [InlineData("ring", "color")]
    [InlineData("ring", "effect")]
    [InlineData("ring", "source")]
    public void Essentials_are_in_basics(string owner, string path) =>
        Assert.Contains(owner, PlainLabels.For(owner, path)!.BasicFor);

    [Fact]
    public void List_indices_resolve_without_hiding_unknown_keys()
    {
        Assert.Equal(PlainLabels.For("graph", "series[0].style.fill"), PlainLabels.For("graph", "series[12].style.fill"));
        Assert.Null(PlainLabels.For("graph", "series[1].future_key"));
        Assert.Null(PlainLabels.For("future_widget", "size"));
        Assert.Null(PlainLabels.For("gauge", "future_key"));
        Assert.Null(PlainLabels.For("graph", "series[-1].source"));
    }

    [Fact]
    public void Transparency_explains_the_opaque_percentage() =>
        Assert.Contains("0–100% opaque", PlainLabels.For("text", "style.opacity")!.Tooltip);
}
