using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// Each command is one or more LayoutDocument edits made as one undo step, which undo and redo swap back and forth as
/// whole texts, with the selection as it was either side.
public class EditorCommandTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - type: text\n" +
        "    at: [10, 10]\n" +
        "    size: [200, 40]\n" +
        "    text: \"CPU {cpu.load:0}%\"\n" +
        "    style: { color: '#FF0000' }\n" +
        "  - { type: shape, shape: rect, at: [300, 10], size: [40, 40], style: { fill: { linear: [\"#000000\", \"#FFFFFF\"] } } }\n" +
        "  - type: group\n" +
        "    at: [400, 100]\n" +
        "    size: [300, 200]\n" +
        "    children:\n" +
        "      - { type: text, at: [0, 0], size: [100, 20], text: A }\n" +
        "      - { type: text, at: [0, 30], size: [100, 20], text: B }\n";

    static YamlPath P(string path) => YamlPath.Parse(path);

    /// Applies `command` to the layout and checks the text it leaves, then that undo and redo swap the whole texts.
    static void AssertStep(EditorCommand command, string expected, string layout = Layout)
    {
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();

        session.Apply(command);

        Assert.Equal(expected, session.Document.Text);
        Assert.Null(session.LoadError);
        Assert.True(session.CanUndo);
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
        Assert.True(session.Redo());
        Assert.Equal(expected, session.Document.Text);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Setting_a_single_value_keeps_its_quoting() =>
        AssertStep(new SetValue(P("widgets[0].style.color"), "#00FF00"), Layout.Replace("'#FF0000'", "'#00FF00'"));

    [Fact]
    public void Setting_a_list_replaces_the_value() =>
        AssertStep(new SetValue(P("widgets[0].at"), new YamlList([YamlValue.Number(20), YamlValue.Number(30.5)])),
            Layout.Replace("at: [10, 10]", "at: [20, 30.5]"));

    [Fact]
    public void Setting_a_colour_over_a_gradient_replaces_the_mapping() =>
        AssertStep(new SetValue(P("widgets[1].style.fill"), "#123456"),
            Layout.Replace("fill: { linear: [\"#000000\", \"#FFFFFF\"] }", "fill: \"#123456\""));

    [Fact]
    public void Setting_a_key_the_widget_has_not_adds_it() =>
        AssertStep(new SetValue(P("widgets[0].style.size"), "30"),
            Layout.Replace("style: { color: '#FF0000' }", "style: { color: '#FF0000', size: 30 }"));

    [Fact]
    public void Setting_a_key_inside_blocks_the_widget_has_not_adds_them_too()
    {
        AssertStep(new SetValue(P("widgets[1].style.stroke.width"), "2"),
            Layout.Replace("\"#FFFFFF\"] } } }", "\"#FFFFFF\"] }, stroke: { width: 2 } } }"));
        AssertStep(new SetValue(P("widgets[2].children[0].style.size"), "12"),
            Layout.Replace("text: A }", "text: A, style: { size: 12 } }"));
    }

    /// A list of mappings is written in block style under a key, which can't go inside `{ }` or `[ ]`: there it is
    /// written on one line.
    [Fact]
    public void A_list_of_mappings_inside_braces_is_written_on_one_line()
    {
        const string flow = "canvas: { orientation: landscape }\nwidgets:\n  - { type: value, at: [0, 0], size: [100, 40], source: cpu.load }\n";
        var spans = new YamlList([YamlMap.Empty.With("text", "{0}"), YamlMap.Empty.With("text", "%")]);
        var written = flow.Replace("cpu.load }", "cpu.load, spans: [{ text: \"{0}\" }, { text: \"%\" }] }");
        AssertStep(new SetValue(P("widgets[0].spans"), spans), written, flow);

        var one = new YamlList([YamlMap.Empty.With("text", "{0} %")]);
        AssertStep(new SetValue(P("widgets[0].spans"), one), written.Replace("[{ text: \"{0}\" }, { text: \"%\" }]", "[{ text: \"{0} %\" }]"), written);
        AssertStep(new SetValue(P("widgets[0].style.stroke"), YamlMap.Empty.With("width", "2")),
            flow.Replace("cpu.load }", "cpu.load, style: { stroke: { width: 2 } } }"), flow);
    }

    [Fact]
    public void A_list_of_mappings_in_a_block_widget_is_written_in_block_style() =>
        AssertStep(new SetValue(P("widgets[0].spans"), new YamlList([YamlMap.Empty.With("text", "{0}")])),
            "canvas: { orientation: landscape }\nwidgets:\n  - type: value\n    at: [0, 0]\n    size: [100, 40]\n    source: cpu.load\n    spans:\n      - { text: \"{0}\" }\n",
            "canvas: { orientation: landscape }\nwidgets:\n  - type: value\n    at: [0, 0]\n    size: [100, 40]\n    source: cpu.load\n");

    [Fact]
    public void A_key_is_added_after_the_mappings_last() =>
        AssertStep(new AddKey(P("widgets[0]"), "name", "Load"),
            Layout.Replace("    style: { color: '#FF0000' }\n", "    style: { color: '#FF0000' }\n    name: Load\n"));

    [Fact]
    public void Reset_removes_the_key() =>
        AssertStep(new ResetKey(P("widgets[0].style")), Layout.Replace("    style: { color: '#FF0000' }\n", ""));

    [Fact]
    public void Replacing_the_document_swaps_in_the_new_text() =>
        AssertStep(new ReplaceDocument("canvas: { orientation: portrait }\n", "Changed outside the editor"), "canvas: { orientation: portrait }\n");

    [Fact]
    public void Placing_a_widget_writes_only_the_numbers_that_change_in_their_places() =>
        AssertStep(new PlaceWidgets([new WidgetPlacement(P("widgets[0]"), SKRect.Create(10, 25.5f, 200, 60), 0)]),
            Layout.Replace("at: [10, 10]", "at: [10, 25.5]").Replace("size: [200, 40]", "size: [200, 60]"));

    [Fact]
    public void Placing_a_turned_widget_adds_its_rotate() =>
        AssertStep(new PlaceWidgets([new WidgetPlacement(P("widgets[2].children[1]"), SKRect.Create(0, 30, 100, 20), -45)]) { Description = "Rotate" },
            Layout.Replace("text: B }", "text: B, rotate: -45 }"));

    [Fact]
    public void Nudges_with_one_merge_key_are_one_step()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open(new EditorSessionOptions { Time = new ManualTime() });

        for (var x = 11; x <= 15; x++)
            session.Apply(new PlaceWidgets([new WidgetPlacement(P("widgets[1]"), SKRect.Create(300 + x, 10, 40, 40), 0)]) { MergeKey = "nudge" });

        Assert.Equal(1, session.History.Count);
        Assert.Contains("at: [315, 10]", session.Document.Text);
        session.Undo();
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void A_widget_of_no_known_type_or_into_what_is_not_a_group_is_refused()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        Assert.Throws<ArgumentException>(() => session.Apply(new AddWidget("dial")));
        Assert.Throws<ArgumentException>(() => session.Apply(new AddWidget("text", P("widgets[0]"))));
        Assert.Throws<ArgumentException>(() => session.Apply(new AddWidget("text", P("widgets[7]"))));
        Assert.Equal(Layout, session.Document.Text);
    }

    public static TheoryData<string> WidgetTypes => new(ThemeSchema.Widgets.Names);

    [Theory]
    [MemberData(nameof(WidgetTypes))]
    public void A_new_widget_is_the_catalogs_example_centred_on_the_canvas_on_top_and_selected(string type)
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var example = (YamlMap)YamlValue.Parse(ThemeSchema.Widget(type)!.Example!);
        var size = (YamlList)example["size"]!;
        var (width, height) = (float.Parse(((YamlText)size.Items[0]).Value), float.Parse(((YamlText)size.Items[1]).Value));

        session.Apply(new AddWidget(type));

        var added = Assert.Single(session.Selection);
        Assert.Equal(P("widgets[3]"), added);
        var widget = session.Widget(added)!;
        Assert.Equal(type, widget.Type);
        Assert.Equal(SKRect.Create((1920 - width) / 2, (480 - height) / 2, width, height), widget.Bounds);
        Assert.Equal(4, session.Widgets.Count);

        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
        Assert.Empty(session.Selection);
        Assert.True(session.Redo());
        Assert.Equal([added], session.Selection);
    }

    [Fact]
    public void A_new_widget_in_a_group_goes_last_among_its_children_centred_in_the_group()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        session.Apply(new AddWidget("bar", P("widgets[2]")));

        var added = Assert.Single(session.Selection);
        Assert.Equal(P("widgets[2].children[2]"), added);
        var bar = session.Widget(added)!;
        Assert.Equal(SKRect.Create(30, 92, 240, 16), bar.LocalBounds);
        Assert.Equal(SKRect.Create(430, 192, 240, 16), bar.Bounds);
    }

    [Fact]
    public void A_new_widget_in_a_theme_without_widgets_starts_the_list()
    {
        const string empty = "canvas: { orientation: portrait }\n";
        using var folder = TempTheme.With(empty);
        using var session = folder.Open();

        session.Apply(new AddWidget("text"));

        Assert.StartsWith(empty + "widgets:\n", session.Document.Text);
        Assert.Equal(SKRect.Create(120, 940, 240, 40), Assert.Single(session.Widgets).Bounds);
    }

    [Fact]
    public void Deleting_widgets_removes_each_once_with_a_groups_children_going_with_it()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        YamlPath[] chosen = [P("widgets[0]"), P("widgets[2].children[1]"), P("widgets[2]")];
        session.Select(chosen);

        session.Apply(new DeleteWidgets(chosen));

        Assert.Equal(
            "canvas: { orientation: landscape }\nwidgets:\n" +
            "  - { type: shape, shape: rect, at: [300, 10], size: [40, 40], style: { fill: { linear: [\"#000000\", \"#FFFFFF\"] } } }\n",
            session.Document.Text);
        Assert.Empty(session.Selection);
        Assert.Equal("shape", Assert.Single(session.Widgets).Type);

        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
        Assert.Equal(chosen, session.Selection);
    }

    [Fact]
    public void Deleting_a_path_that_is_not_a_widget_is_refused()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        Assert.Throws<ArgumentException>(() => session.Apply(new DeleteWidgets([P("widgets[2].size")])));
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void A_refused_edit_changes_nothing()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var theme = session.Theme;

        Assert.Throws<LayoutEditException>(() => session.Apply(new SetValue(P("widgets[9].at"), "1")));
        Assert.Throws<LayoutEditException>(() => session.Apply(new ResetKey(P("widgets[0].nothing"))));

        Assert.Equal(Layout, session.Document.Text);
        Assert.Same(theme, session.Theme);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void An_edit_that_would_break_the_theme_is_refused_with_the_error_and_changes_nothing()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var theme = session.Theme;

        var error = Assert.Throws<ThemeException>(() => session.Apply(new SetValue(P("widgets[0].size[0]"), "wide")));

        Assert.Equal(5, error.Line);
        Assert.Equal(Layout, session.Document.Text);
        Assert.Same(theme, session.Theme);
        Assert.False(theme!.IsDisposed);
        Assert.Null(session.LoadError);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void An_edit_that_changes_nothing_is_no_step()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.Apply(new SetValue(P("widgets[0].at[0]"), "10"));

        Assert.False(session.CanUndo);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Each_step_says_what_changed()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var changes = new List<SessionChanges>();
        var selections = 0;
        session.Changed += (_, what) => changes.Add(what);
        session.SelectionChanged += (_, _) => selections++;

        session.Apply(new SetValue(P("widgets[0].at[0]"), "12"));
        session.Select([P("widgets[1]")]);
        session.Select([P("widgets[1]")]);
        session.Apply(new AddWidget("text"));
        session.Undo();

        Assert.Equal(
        [
            SessionChanges.Document | SessionChanges.Theme,
            SessionChanges.Selection,
            SessionChanges.Document | SessionChanges.Theme | SessionChanges.Selection,
            SessionChanges.Document | SessionChanges.Theme | SessionChanges.Selection,
        ], changes);
        Assert.Equal(3, selections);
    }

    [Fact]
    public void Selecting_takes_widgets_once_each_and_refuses_what_is_not_one()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        session.Select([P("widgets[2].children[1]"), P("widgets[0]"), P("widgets[2].children[1]")]);

        Assert.Equal([P("widgets[2].children[1]"), P("widgets[0]")], session.Selection);
        Assert.Throws<ArgumentException>(() => session.Select([P("widgets[7]")]));
        Assert.Throws<ArgumentException>(() => session.Select([P("widgets[0].style")]));
        Assert.Equal([P("widgets[2].children[1]"), P("widgets[0]")], session.Selection);
    }

    [Fact]
    public void After_a_step_the_selection_keeps_the_widgets_that_are_still_there()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        session.Select([P("widgets[0]"), P("widgets[2].children[1]")]);

        session.Apply(new ReplaceDocument(Layout.Replace("      - { type: text, at: [0, 30], size: [100, 20], text: B }\n", ""), "Changed outside the editor"));

        Assert.Equal([P("widgets[0]")], session.Selection);
        session.Undo();
        Assert.Equal([P("widgets[0]"), P("widgets[2].children[1]")], session.Selection);
    }
}
