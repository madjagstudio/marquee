using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// The layers panel's edits, each one undo step whose text loads as a theme: rename, hide and lock, reorder, moving into
/// and out of groups without the widget moving on screen, grouping and ungrouping, and copy, paste and duplicate.
public class LayerCommandTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "background:\n" +
        "  - fill: \"#000000\"\n" +
        "widgets:\n" +
        "  # The title.\n" +
        "  - type: text\n" +
        "    at: [100, 100]\n" +
        "    size: [200, 40]\n" +
        "    text: CPU # what it says\n" +
        "  - { type: shape, shape: rect, at: [450, 150], size: [40, 40] }\n" +
        "  - type: group\n" +
        "    at: [400, 100]\n" +
        "    size: [300, 200]\n" +
        "    children:\n" +
        "      - { type: text, at: [10, 10], size: [100, 20], text: A }\n" +
        "      - { type: text, at: [10, 40], size: [100, 20], text: B }\n";

    static YamlPath P(string path) => YamlPath.Parse(path);

    /// Applies `command` to `layout`, checks it leaves `expected`, which loads, and the selection `selected`, then that
    /// it undoes to `layout` in one step and redoes.
    static void AssertStep(EditorCommand command, string expected, IReadOnlyList<YamlPath>? selected = null,
        string layout = Layout)
    {
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();

        session.Apply(command);

        Assert.Equal(expected, session.Document.Text);
        Assert.Null(session.LoadError);
        if (selected is not null) Assert.Equal(selected, session.Selection);
        Assert.Equal(1, session.History.Count);
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
        Assert.True(session.Redo());
        Assert.Equal(expected, session.Document.Text);
    }

    [Fact]
    public void Renaming_writes_name_and_clearing_the_name_removes_it()
    {
        AssertStep(new RenameLayer(P("widgets[1]"), "Hot spot"),
            Layout.Replace("size: [40, 40] }", "size: [40, 40], name: Hot spot }"));
        AssertStep(new RenameLayer(P("widgets[0]"), "  Title: CPU  "),
            Layout.Replace("    text: CPU # what it says\n", "    text: CPU # what it says\n    name: \"Title: CPU\"\n"));

        var named = Layout.Replace("size: [40, 40] }", "size: [40, 40], name: Spot }");
        AssertStep(new RenameLayer(P("widgets[1]"), "Glow"), named.Replace("Spot", "Glow"), layout: named);
        AssertStep(new RenameLayer(P("widgets[1]"), " "), Layout, layout: named);
    }

    [Fact]
    public void Hiding_and_locking_write_the_keys_and_showing_and_unlocking_remove_them()
    {
        AssertStep(new SetLayerFlag([P("widgets[1]"), P("widgets[0]")], LayerFlag.Hidden, true),
            Layout.Replace("size: [40, 40] }", "size: [40, 40], hidden: true }")
                .Replace("    text: CPU # what it says\n", "    text: CPU # what it says\n    hidden: true\n"));
        AssertStep(new SetLayerFlag([P("background[0]")], LayerFlag.Locked, true),
            Layout.Replace("  - fill: \"#000000\"\n", "  - fill: \"#000000\"\n    locked: true\n"));

        var hidden = Layout.Replace("size: [40, 40] }", "size: [40, 40], hidden: false, locked: true }");
        AssertStep(new SetLayerFlag([P("widgets[1]")], LayerFlag.Hidden, true), hidden.Replace("hidden: false", "hidden: true"),
            layout: hidden);
        AssertStep(new SetLayerFlag([P("widgets[1]")], LayerFlag.Locked, false), hidden.Replace(", locked: true", ""),
            layout: hidden);
        Assert.Equal("Unlock", new SetLayerFlag([], LayerFlag.Locked, false).Label);
        Assert.Equal("Hide", new SetLayerFlag([], LayerFlag.Hidden, true).Label);
    }

    [Fact]
    public void Reordering_moves_the_widget_with_its_comments()
    {
        AssertStep(new MoveLayer(P("widgets[0]"), null, 1),
            "canvas: { orientation: landscape }\n" +
            "background:\n" +
            "  - fill: \"#000000\"\n" +
            "widgets:\n" +
            "  - { type: shape, shape: rect, at: [450, 150], size: [40, 40] }\n" +
            "  # The title.\n" +
            "  - type: text\n" +
            "    at: [100, 100]\n" +
            "    size: [200, 40]\n" +
            "    text: CPU # what it says\n" +
            "  - type: group\n" +
            "    at: [400, 100]\n" +
            "    size: [300, 200]\n" +
            "    children:\n" +
            "      - { type: text, at: [10, 10], size: [100, 20], text: A }\n" +
            "      - { type: text, at: [10, 40], size: [100, 20], text: B }\n",
            [P("widgets[1]")]);
    }

    [Fact]
    public void Moving_into_a_group_keeps_the_widget_where_it_is_on_screen()
    {
        // The rect at (450, 150) on the canvas is at (50, 50) in the group at (400, 100); it goes on top of A and B.
        AssertStep(new MoveLayer(P("widgets[1]"), P("widgets[2]"), 2),
            Layout.Replace("  - { type: shape, shape: rect, at: [450, 150], size: [40, 40] }\n", "")
                + "      - { type: shape, shape: rect, at: [50, 50], size: [40, 40] }\n",
            [P("widgets[1].children[2]")]);
        Assert.Equal("Move into group", new MoveLayer(P("widgets[1]"), P("widgets[2]"), 2).Label);
    }

    [Fact]
    public void Moving_out_of_a_group_keeps_the_widget_where_it_is_on_screen()
    {
        AssertStep(new MoveLayer(P("widgets[2].children[1]"), null, 3),
            Layout.Replace("      - { type: text, at: [10, 40], size: [100, 20], text: B }\n", "")
                + "  - { type: text, at: [410, 140], size: [100, 20], text: B }\n",
            [P("widgets[3]")]);
        Assert.Equal("Move out of group", new MoveLayer(P("widgets[2].children[1]"), null, 3).Label);
    }

    [Fact]
    public void Moving_out_of_a_turned_group_turns_the_widget_as_the_group_did()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - type: group\n" +
            "    at: [1000, 100]\n" +
            "    size: [400, 200]\n" +
            "    rotate: 90\n" +
            "    children:\n" +
            "      - { type: shape, shape: rect, at: [0, 0], size: [100, 20] }\n" +
            "      - { type: shape, shape: rect, at: [300, 0], size: [100, 200] }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();
        var before = CanvasGeometry.Of(session).CornersOf(P("widgets[0].children[0]"));

        session.Apply(new MoveLayer(P("widgets[0].children[0]"), null, 1));

        // Its centre, (1050, 110) before the group turns, is (1290, 50) after; and it turns 90° itself now.
        Assert.Contains("  - { type: shape, shape: rect, at: [1240, 40], size: [100, 20], rotate: 90 }\n", session.Document.Text);
        var after = CanvasGeometry.Of(session).CornersOf(P("widgets[1]"));
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(before[i].X, after[i].X, 2);
            Assert.Equal(before[i].Y, after[i].Y, 2);
        }
    }

    /// Each trip into a turned group and out again rounds the position to two decimals; whole numbers stay whole, and
    /// the widget is left without the `rotate: 0` the trip back would write.
    [Fact]
    public void Moving_in_and_out_of_a_turned_group_gives_back_the_file()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - { type: group, at: [300, 100], size: [400, 300], rotate: 33, children: [] }\n" +
            "  - { type: shape, shape: rect, at: [500, 200], size: [37, 23], style: { fill: \"#FF0000\" } }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();

        for (var i = 0; i < 100; i++)
        {
            session.Apply(new MoveLayer(P("widgets[1]"), P("widgets[0]"), 0));
            session.Apply(new MoveLayer(P("widgets[0].children[0]"), null, 1));
        }

        Assert.Equal(layout, session.Document.Text);
    }

    [Fact]
    public void Moving_a_group_into_itself_is_refused()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        Assert.Throws<ArgumentException>(() => session.Apply(new MoveLayer(P("widgets[2]"), P("widgets[2]"), 0)));
        Assert.Throws<ArgumentException>(() => session.Apply(new MoveLayer(P("widgets[0]"), P("widgets[1]"), 0)));
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void Grouping_wraps_the_widgets_in_a_group_round_them_with_their_positions_rebased()
    {
        AssertStep(new GroupWidgets([P("widgets[1]"), P("widgets[0]")]),
            "canvas: { orientation: landscape }\n" +
            "background:\n" +
            "  - fill: \"#000000\"\n" +
            "widgets:\n" +
            "  - type: group\n" +
            "    at: [100, 100]\n" +
            "    size: [390, 90]\n" +
            "    children:\n" +
            "      # The title.\n" +
            "      - type: text\n" +
            "        at: [0, 0]\n" +
            "        size: [200, 40]\n" +
            "        text: CPU # what it says\n" +
            "      - { type: shape, shape: rect, at: [350, 50], size: [40, 40] }\n" +
            "  - type: group\n" +
            "    at: [400, 100]\n" +
            "    size: [300, 200]\n" +
            "    children:\n" +
            "      - { type: text, at: [10, 10], size: [100, 20], text: A }\n" +
            "      - { type: text, at: [10, 40], size: [100, 20], text: B }\n",
            [P("widgets[0]")]);
    }

    /// A line's box has no height, but a group's size must be at least 1 each way.
    [Fact]
    public void Grouping_a_line_gives_the_group_a_box_at_least_1_px_each_way() =>
        AssertStep(new GroupWidgets([P("widgets[0]")]),
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - type: group\n" +
            "    at: [10, 10]\n" +
            "    size: [200, 1]\n" +
            "    children:\n" +
            "      - { type: shape, shape: line, at: [0, 0], size: [200, 0], style: { stroke: { width: 2, color: \"#FFFFFF\" } } }\n",
            [P("widgets[0]")],
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - { type: shape, shape: line, at: [10, 10], size: [200, 0], style: { stroke: { width: 2, color: \"#FFFFFF\" } } }\n");

    [Fact]
    public void Ungrouping_puts_the_children_back_where_they_are_on_screen()
    {
        AssertStep(new UngroupWidgets([P("widgets[2]")]),
            Layout.Replace(
                "  - type: group\n    at: [400, 100]\n    size: [300, 200]\n    children:\n" +
                "      - { type: text, at: [10, 10], size: [100, 20], text: A }\n" +
                "      - { type: text, at: [10, 40], size: [100, 20], text: B }\n",
                "  - { type: text, at: [410, 110], size: [100, 20], text: A }\n" +
                "  - { type: text, at: [410, 140], size: [100, 20], text: B }\n"),
            [P("widgets[2]"), P("widgets[3]")]);
    }

    [Fact]
    public void Grouping_then_ungrouping_gives_back_the_file()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        session.Apply(new GroupWidgets([P("widgets[0]"), P("widgets[1]")]));
        session.Apply(new UngroupWidgets(session.Selection));

        Assert.Equal(Layout, session.Document.Text);
        Assert.Equal([P("widgets[0]"), P("widgets[1]")], session.Selection);
    }

    [Fact]
    public void Ungrouping_nested_groups_together_keeps_every_widget_in_place()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - type: group\n" +
            "    at: [100, 100]\n" +
            "    size: [600, 300]\n" +
            "    children:\n" +
            "      - { type: text, at: [0, 0], size: [100, 20], text: A }\n" +
            "      - type: group\n" +
            "        at: [200, 100]\n" +
            "        size: [300, 150]\n" +
            "        rotate: 90\n" +
            "        children:\n" +
            "          - { type: text, at: [10, 10], size: [100, 20], text: B }\n" +
            "          - { type: text, at: [10, 40], size: [100, 20], text: C }\n" +
            "      - { type: text, at: [0, 280], size: [100, 20], text: D }\n" +
            "  - { type: text, at: [800, 0], size: [100, 20], text: E }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();
        var geometry = CanvasGeometry.Of(session);
        var before = new[] { "widgets[0].children[0]", "widgets[0].children[1].children[0]", "widgets[0].children[1].children[1]",
            "widgets[0].children[2]", "widgets[1]" }.Select(path => geometry.CornersOf(P(path))).ToList();

        session.Apply(new UngroupWidgets([P("widgets[0]"), P("widgets[0].children[1]")]));

        Assert.Equal([P("widgets[0]"), P("widgets[1]"), P("widgets[2]"), P("widgets[3]")], session.Selection);
        var after = CanvasGeometry.Of(session);
        for (var i = 0; i < 5; i++)
        {
            var corners = after.CornersOf(P($"widgets[{i}]"));
            for (var c = 0; c < 4; c++)
            {
                Assert.Equal(before[i][c].X, corners[c].X, 1);
                Assert.Equal(before[i][c].Y, corners[c].Y, 1);
            }
        }
    }

    [Fact]
    public void Copy_and_paste_keep_the_widgets_yaml_and_comments_20_px_down_and_right()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var clipboard = WidgetClipboard.Copy(session.Document, [P("widgets[1]"), P("widgets[0]")]);
        Assert.Equal(2, WidgetClipboard.Items(clipboard)!.Count);
        session.Apply(new PasteWidgets(clipboard));

        // Each keeps its own style: the rect stays a one-liner, though it lands beside a block mapping.
        Assert.Equal(Layout +
            "  # The title.\n" +
            "  - type: text\n" +
            "    at: [120, 120]\n" +
            "    size: [200, 40]\n" +
            "    text: CPU # what it says\n" +
            "  - { type: shape, shape: rect, at: [470, 170], size: [40, 40] }\n",
            session.Document.Text);
        Assert.Equal([P("widgets[3]"), P("widgets[4]")], session.Selection);
        Assert.Equal("Paste", session.History.NextUndo!.Label);
        session.Undo();
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void Paste_lands_in_the_group_being_worked_in()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        session.Apply(new PasteWidgets(WidgetClipboard.Copy(session.Document, [P("widgets[1]")]), P("widgets[2]")));

        Assert.Equal(Layout + "      - { type: shape, shape: rect, at: [470, 170], size: [40, 40] }\n", session.Document.Text);
        Assert.Equal([P("widgets[2].children[2]")], session.Selection);
    }

    [Fact]
    public void Widget_yaml_from_elsewhere_pastes_and_anything_else_does_not()
    {
        Assert.Single(WidgetClipboard.Items("type: text\nat: [0, 0]\nsize: [10, 10]\ntext: Hi\n")!);
        Assert.Equal(2, WidgetClipboard.Items("- { type: text, at: [0, 0], size: [10, 10], text: A }\r\n- type: bar\r\n  at: [0, 0]\r\n")!.Count);
        Assert.Null(WidgetClipboard.Items("hello"));
        Assert.Null(WidgetClipboard.Items("- { type: nonsense }\n"));
        Assert.Null(WidgetClipboard.Items("canvas: { orientation: landscape }\n"));
        Assert.Null(WidgetClipboard.Items("[unclosed"));

        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        Assert.Throws<ArgumentException>(() => session.Apply(new PasteWidgets("hello")));
    }

    [Fact]
    public void Duplicate_puts_each_copy_just_in_front_of_its_widget()
    {
        // Each copy keeps its widget's YAML: the rect's stays a one-liner, though the group after it is a block mapping.
        AssertStep(new DuplicateWidgets([P("widgets[2].children[0]"), P("widgets[1]")]),
            Layout.Replace("  - { type: shape, shape: rect, at: [450, 150], size: [40, 40] }\n",
                    "  - { type: shape, shape: rect, at: [450, 150], size: [40, 40] }\n" +
                    "  - { type: shape, shape: rect, at: [470, 170], size: [40, 40] }\n")
                .Replace("      - { type: text, at: [10, 10], size: [100, 20], text: A }\n",
                    "      - { type: text, at: [10, 10], size: [100, 20], text: A }\n" +
                    "      - { type: text, at: [30, 30], size: [100, 20], text: A }\n"),
            [P("widgets[3].children[1]"), P("widgets[2]")]);
    }

    [Fact]
    public void Duplicate_and_paste_keep_a_one_liners_quoting_and_comment_beside_block_items()
    {
        const string oneLiner = "{ type: text, at: [0, 50], size: [100, 40], text: 'B # not a comment' }   # keep me\n";
        const string copy = "  - { type: text, at: [20, 70], size: [100, 40], text: 'B # not a comment' }   # keep me\n";
        const string layout = "canvas: { orientation: landscape }\nwidgets:\n  - " + oneLiner +
            "  - type: text\n    at: [0, 0]\n    size: [100, 40]\n    text: A\n";

        AssertStep(new DuplicateWidgets([P("widgets[0]")]), layout.Replace("  - " + oneLiner, "  - " + oneLiner + copy),
            [P("widgets[1]")], layout);

        using var folder = TempTheme.With(layout);
        using var session = folder.Open();
        session.Apply(new PasteWidgets(WidgetClipboard.Copy(session.Document, [P("widgets[0]")])));
        Assert.Equal(layout + copy, session.Document.Text);
    }

    [Fact]
    public void Delete_removes_the_widgets_with_their_comments()
    {
        AssertStep(new DeleteWidgets([P("widgets[0]"), P("widgets[2].children[1]")]),
            Layout.Replace("  # The title.\n  - type: text\n    at: [100, 100]\n    size: [200, 40]\n    text: CPU # what it says\n", "")
                .Replace("      - { type: text, at: [10, 40], size: [100, 20], text: B }\n", ""),
            []);
    }

    [Fact]
    public void Layer_edits_work_on_the_example_themes()
    {
        foreach (var name in TempTheme.Examples)
        {
            using var folder = TempTheme.Example(name);
            using var session = folder.Open();
            var original = session.Document.Text;
            var top = LayersModel.Of(session).Widgets[0].Path;

            session.Apply(new SetLayerFlag([top], LayerFlag.Locked, true));
            session.Apply(new RenameLayer(top, "Top"));
            session.Apply(new DuplicateWidgets([top]));
            session.Apply(new PasteWidgets(WidgetClipboard.Copy(session.Document, [top])));
            session.Apply(new GroupWidgets(session.Selection.Append(top).ToList()));
            session.Apply(new UngroupWidgets(session.Selection));
            Assert.Null(session.LoadError);

            while (session.Undo()) { }
            Assert.Equal(original, session.Document.Text);
        }
    }
}
