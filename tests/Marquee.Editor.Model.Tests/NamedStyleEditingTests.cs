// tests/Marquee.Editor.Model.Tests/NamedStyleEditingTests.cs
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Named styles: renamed everywhere they're named, deleted without changing a widget's look, and made from a widget.
public class NamedStyleEditingTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "styles:\n" +
        "  meter: { color: \"#FF0000\", size: 20 } # the cards\n" +
        "  big: { extends: meter, size: 40 }\n" +
        "widgets:\n" +
        "  - { type: text, at: [0, 0], size: [100, 40], text: a, style: meter }\n" +
        "  - type: value\n    at: [0, 50]\n    size: [100, 40]\n    source: cpu.temp\n    style: { extends: meter, color: \"#00FF00\" }\n" +
        "    when:\n      - { above: 80, style: meter }\n";

    [Fact]
    public void Renaming_a_style_follows_every_reference_in_one_step()
    {
        using var theme = TempTheme.With(Layout);
        using var session = EditorSession.Open(theme.Folder);

        session.Apply(new RenameStyle("meter", "gauge"));

        Assert.DoesNotContain("meter", session.Document.Text);
        Assert.Contains("gauge: { color: \"#FF0000\", size: 20 } # the cards", session.Document.Text);
        Assert.Contains("extends: gauge, size: 40", session.Document.Text);
        Assert.Null(session.LoadError);
        session.Undo();
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void Deleting_a_used_style_keeps_every_widget_looking_the_same()
    {
        using var theme = TempTheme.With(Layout);
        using var session = EditorSession.Open(theme.Folder);
        var widgets = new[] { YamlPath.Parse("widgets[0]"), YamlPath.Parse("widgets[1]") };
        var before = widgets.Select(w => YamlWriter.Flow(StyleValues.Drawn(session.Document, w))).ToList();
        var rule = YamlWriter.Flow(StyleValues.Effective(session.Document, YamlPath.Parse("widgets[1].when[0].style")));
        var big = YamlWriter.Flow(StyleValues.Named(session.Document, "big"));
        var delete = new DeleteStyle("meter");

        session.Apply(delete);

        Assert.Equal(before, widgets.Select(w => YamlWriter.Flow(StyleValues.Drawn(session.Document, w))).ToList());
        Assert.Equal(rule, YamlWriter.Flow(StyleValues.Effective(session.Document, YamlPath.Parse("widgets[1].when[0].style"))));
        Assert.Equal(big, YamlWriter.Flow(StyleValues.Named(session.Document, "big")));
        Assert.Equal(4, delete.Copied);   // widget 0, widget 1, its rule, and `big`
        Assert.DoesNotContain("meter", session.Document.Text);
    }

    [Fact]
    public void Save_as_style_names_the_widgets_own_style_and_keeps_its_look()
    {
        using var theme = TempTheme.With("canvas: { orientation: landscape }\nwidgets:\n  - { type: text, at: [0, 0], size: [100, 40], text: a, style: { color: \"#123456\", size: 18 } }\n");
        using var session = EditorSession.Open(theme.Folder);
        var widget = YamlPath.Parse("widgets[0]");
        var before = YamlWriter.Flow(StyleValues.Drawn(session.Document, widget));

        session.Apply(new SaveAsStyle(widget, "title"));

        Assert.Equal("title", StyleValues.NameOf(session.Document, widget.Child("style")));
        Assert.Equal(before, YamlWriter.Flow(StyleValues.Drawn(session.Document, widget)));
    }

    [Fact]
    public void A_name_that_is_taken_or_loops_is_refused()
    {
        var document = LayoutDocument.Open(Layout);
        Assert.NotNull(StyleCommands.StyleNameProblem(document, "big"));
        Assert.NotNull(StyleCommands.StyleNameProblem(document, ""));
        Assert.Null(StyleCommands.StyleNameProblem(document, "label"));
        Assert.NotNull(StyleCommands.ExtendsProblem(document, "meter", extends: "big"));   // big extends meter
    }

    [Fact]
    public void A_selected_style_shows_extends_and_the_style_sections()
    {
        using var theme = TempTheme.With(Layout);
        using var session = EditorSession.Open(theme.Folder);

        session.Select([YamlPath.Parse("styles.big")]);
        var model = PropertyModel.For(session);

        Assert.Equal(PropertyTargetKind.Style, model.Target);
        Assert.NotNull(model.Row("extends"));
        Assert.NotEmpty(model.Style);
    }

    [Fact]
    public void New_style_creates_the_section_and_selects_it_with_undo_and_redo()
    {
        const string layout = "canvas: { orientation: landscape }\nwidgets: []\n";
        using var theme = TempTheme.With(layout);
        using var session = theme.Open();

        session.Apply(new NewStyle("a style: blue"));

        Assert.Equal("a style: blue", session.SelectedStyle);
        Assert.True(session.IsNamedStyle(YamlPath.Root.Child("styles").Child("a style: blue")));
        Assert.Equal(PropertyTargetKind.Style, PropertyModel.For(session).Target);
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
        Assert.Null(session.SelectedStyle);
        Assert.False(session.CanUndo);
        Assert.True(session.Redo());
        Assert.Equal("a style: blue", session.SelectedStyle);
    }

    [Fact]
    public void Named_styles_are_selected_alone_and_rename_preserves_selection()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        var meter = YamlPath.Parse("styles.meter");
        session.Select([meter]);
        Assert.Throws<ArgumentException>(() => session.Select([meter, YamlPath.Parse("widgets[0]")]));
        Assert.Throws<ArgumentException>(() => session.Select([meter, YamlPath.Parse("styles.big")]));
        Assert.Throws<ArgumentException>(() => session.Select([YamlPath.Parse("styles.missing")]));
        Assert.False(session.IsNamedStyle(meter.Child("color")));
        Assert.Equal("meter", session.SelectedStyle);

        session.Apply(new RenameStyle("meter", "dial: red"));

        Assert.Equal("dial: red", session.SelectedStyle);
        Assert.Equal(4, StyleUsers.Count(session.Document, "dial: red"));
        Assert.True(session.Undo());
        Assert.Equal("meter", session.SelectedStyle);
        Assert.True(session.Redo());
        Assert.Equal("dial: red", session.SelectedStyle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("big")]
    public void Invalid_names_are_refused_without_a_change(string name)
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        Assert.Throws<PropertyValueException>(() => session.Apply(new NewStyle(name)));
        Assert.Throws<PropertyValueException>(() => session.Apply(new RenameStyle("meter", name)));
        Assert.Throws<PropertyValueException>(() => session.Apply(new SaveAsStyle(YamlPath.Parse("widgets[0]"), name)));
        Assert.Equal(Layout, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Style_rows_include_every_key_and_inherit_from_extends()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        session.Select([YamlPath.Parse("styles.big")]);
        var model = PropertyModel.For(session);
        Assert.Equal(["meter"], model.Row("extends")!.Choices);
        Assert.Equal(new YamlText("#FF0000"), model.Row("style.color")!.Inherited);
        foreach (var key in ThemeSchema.Style.Keys.Where(key => key.Name != "extends"))
        {
            var row = model.Row($"style.{key.Name}");
            Assert.NotNull(row);
            if (key.Kind == SchemaKind.Block)
                foreach (var child in ThemeSchema.Block(key.Of!)!.Keys)
                    Assert.NotNull(model.Row($"style.{key.Name}.{child.Name}"));
        }

        session.Apply(model.Set(model.Row("style.color")!, "#0000FF"));
        Assert.Equal(new YamlText("#0000FF"), StyleValues.Named(session.Document, "big")["color"]);
        Assert.Equal(new YamlText("#FF0000"), StyleValues.Named(session.Document, "meter")["color"]);
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
    }

    [Theory]
    [InlineData("{ color: \"#FF0000\" }", "{}")]
    [InlineData("{ extends: base, color: \"#FF0000\" }", "{ extends: base }")]
    [InlineData("{ stroke: { color: \"#FF0000\" } }", "{}")]
    public void Resetting_the_last_override_keeps_a_named_definition(string style, string expected)
    {
        using var theme = TempTheme.With($"canvas: {{ orientation: landscape }}\nstyles: {{ base: {{}}, selected: {style} }}\nwidgets: []\n");
        using var session = theme.Open();
        session.Select([YamlPath.Parse("styles.selected")]);
        var model = PropertyModel.For(session);

        session.Apply(model.Reset(model.Row(style.Contains("stroke") ? "style.stroke.color" : "style.color")!));

        Assert.Equal(YamlWriter.Flow(YamlValue.Parse(expected)), YamlWriter.Flow(session.Document.ValueAt(YamlPath.Parse("styles.selected"))!));
        Assert.Equal("selected", session.SelectedStyle);
        Assert.Null(session.LoadError);
    }

    [Fact]
    public void Extends_is_editable_resettable_and_rejects_indirect_loops()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        session.Apply(new NewStyle("fresh"));
        var model = PropertyModel.For(session);
        session.Apply(model.Set(model.Row("extends")!, "big"));
        Assert.Equal("big", StyleValues.NameOf(session.Document, YamlPath.Parse("styles.fresh")));

        var meter = PropertyModel.ForStyle(session.Document, "meter");
        Assert.NotNull(meter.Validate(meter.Row("extends")!, "fresh"));
        Assert.NotNull(meter.Validate(meter.Row("extends")!, "meter"));
        Assert.NotNull(meter.Validate(meter.Row("extends")!, "missing"));
        Assert.Throws<PropertyValueException>(() => meter.Set(meter.Row("extends")!, "fresh"));
        model = PropertyModel.For(session);
        session.Apply(model.Reset(model.Row("extends")!));
        Assert.Null(StyleValues.NameOf(session.Document, YamlPath.Parse("styles.fresh")));
        Assert.Equal("fresh", session.SelectedStyle);
        Assert.Null(StyleCommands.ExtendsProblem(session.Document, "meter", null));
        Assert.NotNull(StyleCommands.StyleNameProblem(LayoutDocument.Open("styles: { old: { extends: new } }"), "new", "old"));
    }

    [Theory]
    [InlineData("{ extends: meter, color: \"#123456\" }")]
    [InlineData("meter")]
    public void Save_as_keeps_the_parent_and_overrides_in_one_undo_step(string style)
    {
        using var theme = TempTheme.With($"canvas: {{ orientation: landscape }}\nstyles: {{ meter: {{ color: \"#FF0000\", size: 20 }} }}\nwidgets:\n  - {{ type: text, at: [0, 0], size: [100, 40], text: a, style: {style} }}\n");
        using var session = theme.Open();
        var widget = YamlPath.Parse("widgets[0]");
        session.Select([widget]);
        var before = YamlWriter.Flow(StyleValues.Drawn(session.Document, widget));
        var text = session.Document.Text;
        session.Apply(new SaveAsStyle(widget, "saved"));
        Assert.Equal("meter", StyleValues.NameOf(session.Document, YamlPath.Parse("styles.saved")));
        Assert.Equal(before, YamlWriter.Flow(StyleValues.Drawn(session.Document, widget)));
        Assert.Equal([widget], session.Selection);
        Assert.True(session.Undo());
        Assert.Equal(text, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Users_include_nested_groups_and_rules_and_delete_merges_blocks()
    {
        const string layout = "canvas: { orientation: landscape }\n" +
            "styles: { base: { stroke: { color: \"#FF0000\", width: 2 } }, child: { extends: base, stroke: { width: 4 } } }\n" +
            "widgets:\n  - type: group\n    at: [0, 0]\n    size: [200, 200]\n    style: base\n    children:\n" +
            "      - type: group\n        at: [0, 0]\n        size: [100, 100]\n        children:\n" +
            "          - type: value\n            at: [0, 0]\n            size: [100, 40]\n            source: cpu.load\n" +
            "            style: { extends: base, stroke: { width: 8 } }\n            when:\n" +
            "              - { above: 80, style: { extends: base, stroke: { width: 10 } } }\n";
        using var theme = TempTheme.With(layout);
        using var session = theme.Open();
        var users = StyleUsers.Of(session.Document, "base");
        Assert.Equal(new[] { "widgets[0].style", "widgets[0].children[0].children[0].style",
            "widgets[0].children[0].children[0].when[0].style", "styles.child" }, users.Select(p => p.ToString()));
        var before = users.Select(p => YamlWriter.Flow(StyleValues.Effective(session.Document, p))).ToList();
        session.Apply(new RenameStyle("base", "renamed"));
        Assert.Equal(4, StyleUsers.Count(session.Document, "renamed"));
        Assert.DoesNotContain("base", session.Document.Text);
        Assert.True(session.Undo());
        var delete = new DeleteStyle("base");
        session.Apply(delete);
        Assert.Equal(4, delete.Copied);
        Assert.Equal(before, users.Select(p => YamlWriter.Flow(StyleValues.Effective(session.Document, p))).ToList());
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Delete_removes_an_empty_section_and_undo_restores_selection()
    {
        using var theme = TempTheme.With("canvas: { orientation: landscape }\nstyles: { unused: {} }\nwidgets: []\n");
        using var session = theme.Open();
        session.Select([YamlPath.Parse("styles.unused")]);
        var delete = new DeleteStyle("unused");
        session.Apply(delete);
        Assert.Equal(0, delete.Copied);
        Assert.False(session.Document.Exists(YamlPath.Root.Child("styles")));
        Assert.Empty(session.Selection);
        Assert.True(session.Undo());
        Assert.Equal("unused", session.SelectedStyle);
    }
}
