using System.Windows.Input;
using Marquee.Editor.Layers;
using Marquee.Editor.Model;
using Marquee.Editor.Tests.TestSupport;
using static Marquee.Editor.Tests.TestSupport.OpenEditor;

namespace Marquee.Editor.Tests;

/// The layers panel, driven through its view-model as the view drives it, on TestEnvironment.Layout: a red box, a blue
/// one, and a group holding a green one, over a black background.
public class LayersPanelTests
{
    static LayerItem Row(OpenEditor editor, string path) => editor.Model.Layers.Find(P(path)) ?? throw new InvalidOperationException(path);

    static string Text(OpenEditor editor) => editor.Session.Document.Text;

    [Fact]
    public void The_tree_runs_front_to_back_with_groups_indented_and_the_background_below() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var layers = editor.Model.Layers;

        Assert.Equal([P("widgets[2]"), P("widgets[2].children[0]"), P("widgets[1]"), P("widgets[0]")], layers.Widgets.Select(row => row.Path));
        Assert.Equal([0, 1, 0, 0], layers.Widgets.Select(row => row.Depth));
        Assert.Equal("Group", layers.Widgets[0].Name);
        Assert.Equal("Shape · rect", layers.Widgets[3].Name);
        Assert.Equal("◆", layers.Widgets[3].Glyph);
        Assert.Equal([P("background[0]")], layers.Background.Select(row => row.Path));

        layers.ToggleExpanded(layers.Widgets[0]);
        Assert.Equal(3, layers.Widgets.Count);
        // Selecting a widget in a folded group, on the canvas, unfolds it.
        editor.Session.Select([P("widgets[2].children[0]")]);
        Assert.Equal(4, layers.Widgets.Count);
        Assert.True(Row(editor, "widgets[2].children[0]").IsSelected);
    });

    [Fact]
    public void Selection_is_shared_with_the_canvas() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var layers = editor.Model.Layers;

        layers.Click(Row(editor, "widgets[1]"), ModifierKeys.None);
        Assert.Equal([P("widgets[1]")], editor.Session.Selection);
        layers.Click(Row(editor, "widgets[0]"), ModifierKeys.Control);
        Assert.Equal([P("widgets[1]"), P("widgets[0]")], editor.Session.Selection);
        layers.Click(Row(editor, "widgets[2]"), ModifierKeys.Shift);
        Assert.Equal([P("widgets[2]"), P("widgets[2].children[0]"), P("widgets[1]"), P("widgets[0]")], editor.Session.Selection);

        editor.Session.Select([P("widgets[1]")]);
        Assert.Equal([P("widgets[1]")], layers.Widgets.Where(row => row.IsSelected).Select(row => row.Path));
        // Background layers aren't selected in E1.
        layers.Click(layers.Background[0], ModifierKeys.None);
        Assert.Equal([P("widgets[1]")], editor.Session.Selection);
    });

    [Fact]
    public void Dragging_a_row_reorders_and_moves_into_and_out_of_groups() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var layers = editor.Model.Layers;

        // Above the blue box is in front of it: after it in the file.
        Assert.Equal(new MoveLayer(P("widgets[0]"), null, 1), layers.DropFor(Row(editor, "widgets[0]"), Row(editor, "widgets[1]"), DropPlace.Above));
        Assert.Null(layers.DropFor(Row(editor, "widgets[0]"), Row(editor, "widgets[1]"), DropPlace.Below));
        Assert.Null(layers.DropFor(Row(editor, "widgets[0]"), Row(editor, "widgets[1]"), DropPlace.Into));
        Assert.Null(layers.DropFor(Row(editor, "widgets[2]"), Row(editor, "widgets[2].children[0]"), DropPlace.Above));

        Assert.True(layers.Drop(Row(editor, "widgets[0]"), Row(editor, "widgets[1]"), DropPlace.Above));
        Assert.StartsWith("  - { type: shape, shape: rect, at: [500, 100]", Text(editor).Split("widgets:\n")[1]);
        Assert.Equal([P("widgets[1]")], editor.Session.Selection);

        // Into the group, on top of its child, at the same place on screen.
        var move = layers.DropFor(Row(editor, "widgets[1]"), Row(editor, "widgets[2]"), DropPlace.Into);
        Assert.Equal(new MoveLayer(P("widgets[1]"), P("widgets[2]"), 1), move);
        Assert.True(layers.Drop(Row(editor, "widgets[1]"), Row(editor, "widgets[2]"), DropPlace.Into));
        Assert.Equal(2, editor.Session.Widgets.Count);
        Assert.Equal(2, editor.Session.Widgets[1].Children.Count);
        Assert.Contains("at: [-900, 0]", Text(editor));

        // And back out, below the blue box.
        Assert.True(layers.Drop(Row(editor, "widgets[1].children[1]"), Row(editor, "widgets[0]"), DropPlace.Below));
        Assert.Equal(3, editor.Session.Widgets.Count);
        Assert.Contains("at: [100, 100]", Text(editor));
        Assert.Equal(3, editor.Session.History.Count);
    });

    [Theory]
    [InlineData(2, false, DropPlace.Above)]
    [InlineData(20, false, DropPlace.Below)]
    [InlineData(4, true, DropPlace.Above)]
    [InlineData(12, true, DropPlace.Into)]
    [InlineData(20, true, DropPlace.Below)]
    public void A_drop_lands_above_below_or_into_by_where_it_is_on_the_row(double y, bool group, DropPlace place) =>
        Assert.Equal(place, LayersViewModel.PlaceAt(y, 24, group));

    [Fact]
    public void Rename_hide_and_lock_write_their_keys() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var layers = editor.Model.Layers;

        var red = Row(editor, "widgets[0]");
        layers.BeginRename(red);
        Assert.True(red.IsRenaming);
        Assert.Equal("Shape · rect", red.EditName);
        // A made-up name left as it was isn't written.
        Assert.False(layers.CommitRename(red));
        layers.BeginRename(red);
        red.EditName = "Red box";
        Assert.True(layers.CommitRename(red));
        Assert.Contains("style: { fill: \"#FF0000\" }, name: Red box }", Text(editor));
        Assert.True(Row(editor, "widgets[0]").IsNamed);

        layers.BeginRename(Row(editor, "widgets[0]"));
        layers.CancelRename(Row(editor, "widgets[0]"));
        layers.BeginRename(Row(editor, "widgets[0]"));
        Row(editor, "widgets[0]").EditName = " ";
        layers.CommitRename(Row(editor, "widgets[0]"));
        Assert.DoesNotContain("name:", Text(editor));

        Assert.True(layers.SetHidden(Row(editor, "widgets[1]"), true));
        Assert.Contains("fill: \"#0000FF\" }, hidden: true }", Text(editor));
        Assert.True(Row(editor, "widgets[1]").Hidden);
        Assert.True(layers.SetLocked(layers.Background[0], true));
        Assert.Contains("  - fill: \"#000000\"\n    locked: true\n", Text(editor));
        Assert.True(layers.SetHidden(Row(editor, "widgets[1]"), false));
        Assert.DoesNotContain("hidden", Text(editor));
    });

    [Fact]
    public void The_menu_groups_ungroups_copies_pastes_duplicates_and_deletes() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var layers = editor.Model.Layers;

        layers.ContextClick(Row(editor, "widgets[0]"));
        layers.Click(Row(editor, "widgets[1]"), ModifierKeys.Control);
        Assert.True(layers.GroupCommand.CanExecute(null));
        Assert.False(layers.UngroupCommand.CanExecute(null));
        layers.GroupCommand.Execute(null);
        Assert.Equal(2, editor.Session.Widgets.Count);

        layers.UngroupCommand.Execute(null);
        Assert.Equal(3, editor.Session.Widgets.Count);

        layers.Click(Row(editor, "widgets[0]"), ModifierKeys.None);
        layers.CopyCommand.Execute(null);
        Assert.Contains("#FF0000", environment.Clipboard.Text);
        Assert.True(layers.PasteCommand.CanExecute(null));
        layers.PasteCommand.Execute(null);
        layers.DuplicateCommand.Execute(null);
        Assert.Equal(5, editor.Session.Widgets.Count);

        layers.DeleteCommand.Execute(null);
        Assert.Equal(4, editor.Session.Widgets.Count);
    });

    [Fact]
    public void A_widget_that_uses_an_anchor_is_read_only_in_the_panel() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var layout = TestEnvironment.Layout.Replace("style: { fill: \"#0000FF\" }", "style: &blue { fill: \"#0000FF\" }");
        using var editor = new OpenEditor(environment, environment.AddTheme("anchors", layout));
        var layers = editor.Model.Layers;

        var blue = Row(editor, "widgets[1]");
        Assert.False(blue.IsEditable);
        Assert.False(layers.SetHidden(blue, true));
        layers.BeginRename(blue);
        Assert.False(blue.IsRenaming);
        Assert.Null(layers.DropFor(blue, Row(editor, "widgets[0]"), DropPlace.Below));
        Assert.True(Row(editor, "widgets[0]").IsEditable);
    });
}
