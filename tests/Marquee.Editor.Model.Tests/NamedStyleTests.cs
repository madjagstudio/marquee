using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// A widget using a named style shows "meter + N overrides". A style change on it becomes an override written in place
/// (`style: { extends: meter, … }`), so other widgets sharing the name don't change; "Clear overrides" returns it to
/// the name. Named styles themselves are only read.
public class NamedStyleTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "styles:\n" +
        "  base: { radius: 3, stroke: { width: 2, color: \"#FFFFFF20\" } }\n" +
        "  meter: { extends: base, fill: \"#36D1DC\", track: \"#FFFFFF10\" }\n" +
        "  hot: { fill: \"#FF5A36\" }\n" +
        "widgets:\n" +
        "  - type: bar\n" +
        "    at: [10, 10]\n" +
        "    size: [200, 20]\n" +
        "    source: cpu.load\n" +
        "    style: meter\n" +
        "  - { type: bar, at: [10, 40], size: [200, 20], source: gpu.load, style: meter }\n" +
        "  - { type: bar, at: [10, 70], size: [200, 20], source: ram.used_pct, style: { extends: meter, radius: 6, stroke: { color: \"#FF0000\" } } }\n" +
        "  - { type: bar, at: [10, 100], size: [200, 20], source: cpu.load }\n";

    static PropertyModel Model(EditorSession session, int widget)
    {
        session.Select([YamlPath.Parse($"widgets[{widget}]")]);
        return PropertyModel.For(session);
    }

    [Fact]
    public void A_widget_shows_its_named_style_and_how_many_keys_it_sets_over_it()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        Assert.Equal("meter", Model(session, 0).StyleUse!.Label);
        Assert.Equal(["radius", "stroke.color"], Model(session, 2).StyleUse!.Overrides);
        Assert.Equal("meter + 2 overrides", Model(session, 2).StyleUse!.Label);
        Assert.Equal("No style", Model(session, 3).StyleUse!.Label);
        Assert.Equal(["base", "meter", "hot"], Model(session, 0).StyleNames);
    }

    [Fact]
    public void A_style_key_the_widget_doesnt_set_shows_its_named_styles_value()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var plain = Model(session, 0);
        Assert.False(plain.Row("style.fill")!.IsSet);
        Assert.Equal(new YamlText("#36D1DC"), plain.Row("style.fill")!.Inherited);
        // Through extends, a block merged key by key.
        Assert.Equal(new YamlText("2"), plain.Row("style.stroke.width")!.Inherited);
        Assert.Equal(new YamlText("3"), plain.Row("style.radius")!.Inherited);

        var over = Model(session, 2);
        Assert.True(over.Row("style.radius")!.IsSet);
        Assert.Equal(new YamlText("6"), over.Row("style.radius")!.Value);
        Assert.Equal(new YamlText("#FF0000"), over.Row("style.stroke.color")!.Value);
        Assert.Equal(new YamlText("2"), over.Row("style.stroke.width")!.Inherited);
    }

    [Fact]
    public void A_style_change_on_a_named_style_becomes_an_override_in_one_step()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var model = Model(session, 0);

        session.Apply(model.Set(model.Row("style.fill")!, "#FF00FF"));

        Assert.Equal(Layout.Replace("    style: meter\n", "    style: { extends: meter, fill: \"#FF00FF\" }\n"), session.Document.Text);
        Assert.Equal("meter + 1 override", PropertyModel.For(session).StyleUse!.Label);
        // The widget beside it, and the named style, are as they were.
        Assert.Contains("source: gpu.load, style: meter }", session.Document.Text);
        Assert.Contains("  meter: { extends: base, fill: \"#36D1DC\", track: \"#FFFFFF10\" }\n", session.Document.Text);

        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void A_block_key_becomes_an_override_of_that_key_alone()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var model = Model(session, 1);

        session.Apply(model.Set(model.Row("style.stroke.width")!, "4"));

        Assert.Contains("source: gpu.load, style: { extends: meter, stroke: { width: 4 } } }", session.Document.Text);
        var after = PropertyModel.For(session);
        Assert.Equal(new YamlText("4"), after.Row("style.stroke.width")!.Value);
        Assert.Equal(new YamlText("#FFFFFF20"), after.Row("style.stroke.color")!.Inherited);
    }

    [Fact]
    public void Clear_overrides_returns_the_widget_to_its_named_style()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        session.Apply(Model(session, 2).ClearOverrides());

        Assert.Equal(Layout.Replace("style: { extends: meter, radius: 6, stroke: { color: \"#FF0000\" } } }", "style: meter }"), session.Document.Text);
        Assert.Equal("meter", PropertyModel.For(session).StyleUse!.Label);
        Assert.Equal("Clear overrides", session.History.NextUndo!.Label);
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);

        // A widget with only its name has nothing to clear.
        var steps = session.History.Count;
        session.Apply(Model(session, 0).ClearOverrides());
        Assert.Equal(steps, session.History.Count);
    }

    [Fact]
    public void Resetting_the_last_override_leaves_the_name()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        session.Apply(Model(session, 2).Reset(Model(session, 2).Row("style.stroke.color")!));
        Assert.Contains("style: { extends: meter, radius: 6 } }", session.Document.Text);
        session.Apply(Model(session, 2).Reset(Model(session, 2).Row("style.radius")!));
        Assert.Contains("source: ram.used_pct, style: meter }", session.Document.Text);
    }

    [Fact]
    public void The_style_dropdown_picks_another_named_style_and_keeps_the_overrides()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        session.Apply(Model(session, 0).PickStyle("hot"));
        Assert.Contains("    style: hot\n", session.Document.Text);

        session.Apply(Model(session, 2).PickStyle("hot"));
        Assert.Contains("style: { extends: hot, radius: 6, stroke: { color: \"#FF0000\" } } }", session.Document.Text);

        session.Apply(Model(session, 3).PickStyle("base"));
        Assert.Contains("source: cpu.load, style: base }", session.Document.Text);

        session.Apply(Model(session, 3).PickStyle(null));
        Assert.Contains("  - { type: bar, at: [10, 100], size: [200, 20], source: cpu.load }\n", session.Document.Text);

        Assert.Throws<PropertyValueException>(() => Model(session, 0).PickStyle("cool"));
    }

    [Fact]
    public void A_widget_without_a_style_gets_one_in_place()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var model = Model(session, 3);

        session.Apply(model.Set(model.Row("style.track")!, "#000000"));

        Assert.Contains("source: cpu.load, style: { track: \"#000000\" } }", session.Document.Text);
        Assert.Equal("1 key set here", PropertyModel.For(session).StyleUse!.Label);
    }

    [Fact]
    public void Showcase_widgets_read_their_overrides()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var ring = session.Widgets.SelectMany(w => w.Children).Single(w => w.Name == "GPU ring");

        var model = PropertyModel.Build(session.Document, [ring]);

        Assert.Equal("meter + 2 overrides", model.StyleUse!.Label);
        Assert.Equal(new YamlText("#FFFFFF10"), model.Row("style.track")!.Inherited);
    }
}
