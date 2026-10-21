using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Writing from the properties panel: a set writes the key at its path, Reset removes it, a slider merges into one step,
/// and several widgets change together as one step.
public class PropertyWritingTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: bar, at: [10, 10], size: [200, 20], source: cpu.load }\n" +
        "  - { type: bar, at: [10, 40], size: [200, 20], source: gpu.load, max: 50 }\n" +
        "  - type: shape\n" +
        "    shape: arc\n" +
        "    at: [300, 10]\n" +
        "    size: [100, 100]\n" +
        "    sweep: 180\n" +
        "    style: { fill: \"#3A7BD5\" }\n" +
        "  - { type: text, at: [500, 10], size: [200, 40], text: Hello }\n" +
        "  - { type: value, at: [500, 60], size: [200, 40], source: cpu.temp, format: \"{0:0}°C\" }\n" +
        "  - type: donut\n" +
        "    at: [800, 10]\n" +
        "    size: [160, 160]\n" +
        "    slices:\n" +
        "      - { source: cpu.load, label: CPU }\n" +
        "      - { source: 100 - cpu.load, label: Idle }\n";

    static PropertyModel Model(EditorSession session, params int[] widgets)
    {
        session.Select(widgets.Select(i => YamlPath.Parse($"widgets[{i}]")));
        return PropertyModel.For(session);
    }

    /// The theme can't leave out a required key, a form's key (`shape`), or the one key of an exactly-one-of (a text's
    /// `text` or `spans`, a graph's `source` or `series`, an item's `source`), so Reset isn't offered there.
    [Fact]
    public void Reset_is_offered_only_for_keys_the_theme_can_leave_out()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var bar = Model(session, 1);
        foreach (var name in new[] { "type", "at", "size", "source" })
        {
            Assert.False(bar.Row(name)!.CanReset, name);
            Assert.Throws<PropertyValueException>(() => bar.Reset(bar.Row(name)!));
        }

        Assert.True(bar.Row("max")!.CanReset);
        Assert.True(bar.Row("style.fill")!.CanReset);
        Assert.False(Model(session, 2).Row("shape")!.CanReset);
        Assert.False(Model(session, 3).Row("text")!.CanReset);
        Assert.False(Model(session, 5).Row("slices[0].source")!.CanReset);
        Assert.True(Model(session, 5).Row("slices[0].label")!.CanReset);

        session.Apply(bar.Reset(bar.Row("max")!));
        Assert.Equal(Layout.Replace(", max: 50", ""), session.Document.Text);
    }

    [Fact]
    public void A_set_writes_the_key_and_undo_takes_it_back()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var model = Model(session, 0);

        session.Apply(model.Set(model.Row("max")!, "80"));

        Assert.Equal(Layout.Replace("source: cpu.load }", "source: cpu.load, max: 80 }"), session.Document.Text);
        Assert.Equal(new YamlText("80"), PropertyModel.For(session).Row("max")!.Value);
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void A_style_key_goes_in_the_widgets_style_in_place()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var bar = Model(session, 0);
        session.Apply(bar.Set(bar.Row("style.stroke.width")!, "2"));
        Assert.Contains("source: cpu.load, style: { stroke: { width: 2 } } }", session.Document.Text);

        var shape = Model(session, 2);
        session.Apply(shape.Set(shape.Row("style.fill")!, "#FF0000"));
        Assert.Contains("    style: { fill: \"#FF0000\" }\n", session.Document.Text);
    }

    [Fact]
    public void Reset_removes_the_key_and_a_style_it_leaves_empty()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var bar = Model(session, 1);
        session.Apply(bar.Reset(bar.Row("max")!));
        Assert.Contains("source: gpu.load }", session.Document.Text);

        var shape = Model(session, 2);
        session.Apply(shape.Reset(shape.Row("style.fill")!));
        Assert.DoesNotContain("style:", session.Document.Text);
        Assert.False(PropertyModel.For(session).Row("style.fill")!.IsSet);

        // Resetting a key that isn't set changes nothing, and makes no step.
        var steps = session.History.Count;
        session.Apply(PropertyModel.For(session).Reset(PropertyModel.For(session).Row("style.fill")!));
        Assert.Equal(steps, session.History.Count);
    }

    [Fact]
    public void A_slider_run_on_one_row_is_one_step()
    {
        var time = new ManualTime();
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open(new EditorSessionOptions { Time = time });
        var model = Model(session, 0);

        foreach (var value in new[] { "60", "70", "80" })
        {
            session.Apply(model.Set(model.Row("max")!, value, merge: true));
            time.Advance(TimeSpan.FromMilliseconds(50));
        }

        Assert.Contains("max: 80", session.Document.Text);
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Choosing_another_form_removes_the_keys_of_the_old_one()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var model = Model(session, 2);

        session.Apply(model.Set(model.Row("shape")!, "rect"));

        Assert.DoesNotContain("sweep", session.Document.Text);
        Assert.Contains("    shape: rect\n", session.Document.Text);
        Assert.Null(PropertyModel.For(session).Row("sweep"));
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void A_line_shape_may_be_0_high()
    {
        using var folder = TempTheme.With(Layout.Replace("shape: arc", "shape: line").Replace("    sweep: 180\n", "")
            .Replace("style: { fill: \"#3A7BD5\" }", "style: { stroke: { width: 2, color: \"#3A7BD5\" } }"));
        using var session = folder.Open();
        var model = Model(session, 2);

        Assert.Equal(0, model.Row("size")!.Min);
        Assert.Null(model.Validate(model.Row("size")!, new YamlList(["100", "0"])));
        Assert.Equal(1, Model(session, 0).Row("size")!.Min);
        Assert.Equal("size must be at least 1", Model(session, 0).Validate(Model(session, 0).Row("size")!, new YamlList(["100", "0"])));
    }

    [Fact]
    public void A_refused_value_says_why_and_writes_nothing()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var model = Model(session, 0);

        var refused = Assert.Throws<PropertyValueException>(() => model.Set(model.Row("smooth")!, "-5"));

        Assert.Equal("smooth must be between 0 and 10000", refused.Reason);
        Assert.Equal(Layout, session.Document.Text);
        Assert.Throws<PropertyValueException>(() => model.Set(model.Row("type")!, "gauge"));
        Assert.Throws<PropertyValueException>(() => model.Reset(model.Row("type")!));
    }

    [Fact]
    public void Several_widgets_show_their_shared_keys_with_mixed_values()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var bars = Model(session, 0, 1);
        Assert.Equal("bar", bars.Type);
        Assert.Equal(new YamlText("200"), (bars.Row("size")!.Value as YamlList)?.Items[0]);
        Assert.True(bars.Row("source")!.IsMixed);
        Assert.Null(bars.Row("source")!.Value);
        Assert.True(bars.Row("max")!.IsMixed);
        Assert.False(bars.Row("max")!.IsSet);
        Assert.False(bars.Row("min")!.IsMixed);
        Assert.Null(bars.StyleUse);
        Assert.Empty(bars.Rules);

        // A bar and a shape share the common keys, and the style keys both draw with.
        var mixed = Model(session, 0, 2);
        Assert.Null(mixed.Type);
        Assert.Equal(["type", "at", "size", "source", "smooth", "rotate", "hidden", "locked", "name"], mixed.Rows.Select(r => r.Name));
        Assert.True(mixed.Row("type")!.IsMixed);
        var style = mixed.Style.SelectMany(s => s.Rows).Select(r => r.Name).ToList();
        Assert.Contains("style.fill", style);
        Assert.DoesNotContain("style.track", style);
        Assert.Equal(["style.stroke.width", "style.stroke.color"], mixed.Row("style.stroke")!.Children.Select(r => r.Name));

        // A donut's slices are its own, so several widgets show no lists.
        Assert.Null(Model(session, 0, 5).Row("slices"));
    }

    [Fact]
    public void A_change_to_several_widgets_is_one_step()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var model = Model(session, 0, 1, 2);

        session.Apply(model.Set(model.Row("style.opacity")!, "0.5"));

        Assert.Contains("source: cpu.load, style: { opacity: 0.5 } }", session.Document.Text);
        Assert.Contains("max: 50, style: { opacity: 0.5 } }", session.Document.Text);
        Assert.Contains("style: { fill: \"#3A7BD5\", opacity: 0.5 }", session.Document.Text);
        Assert.Equal(new YamlText("0.5"), PropertyModel.For(session).Row("style.opacity")!.Value);
        Assert.False(PropertyModel.For(session).Row("style.opacity")!.IsMixed);

        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void A_lists_items_are_added_removed_and_reordered()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var model = Model(session, 5);
        var slices = model.Row("slices")!;
        Assert.Equal(2, slices.Items.Count);
        Assert.False(slices.CanRemove);
        Assert.Throws<PropertyValueException>(() => model.RemoveItem(slices, 0));

        session.Apply(model.AddItem(slices));
        Assert.Contains("      - { source: 100 - cpu.load, label: Idle }\n      - { source: 100 - cpu.load, label: Idle }\n", session.Document.Text);
        Assert.Equal("Add slice", session.History.NextUndo!.Label);

        model = PropertyModel.For(session);
        session.Apply(model.Set(model.Row("slices[2].label")!, "Spare"));
        model = PropertyModel.For(session);
        session.Apply(model.MoveItem(model.Row("slices")!, 2, 0));
        Assert.Contains("    slices:\n      - { source: 100 - cpu.load, label: Spare }\n      - { source: cpu.load, label: CPU }\n", session.Document.Text);

        model = PropertyModel.For(session);
        Assert.True(model.Row("slices")!.CanRemove);
        session.Apply(model.RemoveItem(model.Row("slices")!, 0));
        Assert.Equal(Layout, session.Document.Text);
        Assert.Equal(2, PropertyModel.For(session).Row("slices")!.Items.Count);
    }

    [Fact]
    public void A_text_or_value_widget_splits_into_one_span()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var text = Model(session, 3);
        Assert.True(text.CanSplitIntoSpans);
        session.Apply(text.SplitIntoSpans());
        Assert.Equal(new YamlText("Hello"), PropertyModel.For(session).Row("spans[0].text")!.Value);
        Assert.Null(PropertyModel.For(session).Row("text"));
        Assert.False(PropertyModel.For(session).CanSplitIntoSpans);

        var value = Model(session, 4);
        session.Apply(value.SplitIntoSpans());
        Assert.Equal(new YamlText("{0:0}°C"), PropertyModel.For(session).Row("spans[0].text")!.Value);
        Assert.Null(PropertyModel.For(session).Row("format"));
        Assert.NotNull(PropertyModel.For(session).Row("spans[0].style.size"));

        Assert.True(session.Undo());
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
        Assert.False(Model(session, 0).CanSplitIntoSpans);
    }

    [Fact]
    public void A_key_under_an_anchor_is_shown_but_not_editable()
    {
        const string anchored =
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - { type: bar, at: [10, 10], size: &s [200, 20], source: cpu.load }\n" +
            "  - { type: bar, at: [10, 40], size: *s, source: gpu.load }\n";
        using var folder = TempTheme.With(anchored);
        using var session = folder.Open();
        var model = Model(session, 0);

        Assert.False(model.Row("size")!.IsEditable);
        Assert.Contains("anchor", model.Validate(model.Row("size")!, new YamlList(["10", "10"])));
    }
}
