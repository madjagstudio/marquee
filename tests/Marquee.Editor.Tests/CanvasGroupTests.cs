using System.Windows.Input;
using Marquee.Editor.Canvas;
using Marquee.Editor.Tests.TestSupport;
using static Marquee.Editor.Tests.TestSupport.OpenEditor;

namespace Marquee.Editor.Tests;

public class CanvasGroupTests
{
    [Theory]
    [InlineData(ModifierKeys.None)]
    [InlineData(ModifierKeys.Shift)]
    public void The_group_hint_is_shown_only_on_the_first_selecting_click(ModifierKeys keys) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var reports = new List<string>();
        using var canvas = new CanvasViewModel(_ => true, environment.Clipboard, reports.Add, TimeProvider.System);
        canvas.Attach(editor.Workspace);
        canvas.ZoomMode = ZoomMode.Actual;
        var point = canvas.Transform.ToView(new SkiaSharp.SKPoint(1035, 135));
        canvas.PointerDown(point, keys, 1);
        canvas.PointerUp(point, keys);
        Assert.Single(reports);
        editor.Session.Select([]);
        canvas.PointerDown(point, keys, 1);
        canvas.PointerUp(point, keys);
        Assert.Single(reports);
    });

    [Fact]
    public void Selecting_a_child_in_layers_makes_the_next_click_hit_its_sibling() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var layout = TestEnvironment.Layout + "      - { type: shape, shape: rect, at: [100, 100], size: [40, 40] }\n";
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", layout));
        editor.Zoom();
        editor.Model.Layers.Click(editor.Model.Layers.Find(P("widgets[2].children[0]"))!, ModifierKeys.None);
        editor.Canvas.PointerDown(editor.View(1120, 220), ModifierKeys.None, 1);
        editor.Canvas.PointerUp(editor.View(1120, 220), ModifierKeys.None);
        Assert.Equal([P("widgets[2].children[1]")], editor.Session.Selection);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_selected_child_can_move_or_resize_even_before_the_canvas_enters_its_group(bool resize) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var child = P("widgets[2].children[0]");
        editor.Session.Select([child]);
        // Attach starts at canvas level while the session retains its selection.
        editor.Canvas.Attach(editor.Workspace);
        editor.Zoom();
        Assert.Null(editor.Canvas.Group);
        var start = resize ? editor.View(1060, 160) : editor.View(1035, 135);
        var end = start + (editor.View(20, 20) - editor.View(0, 0));
        editor.Canvas.PointerDown(start, ModifierKeys.Alt, 1);
        editor.Canvas.PointerMove(end, ModifierKeys.Alt);
        editor.Canvas.PointerUp(end, ModifierKeys.Alt);
        Assert.Equal([child], editor.Session.Selection);
        Assert.Equal(resize ? 70 : 50, editor.Session.Widget(child)!.LocalBounds.Width);
        Assert.Equal(resize ? 10 : 30, editor.Session.Widget(child)!.LocalBounds.Left);
        Assert.Equal(1000, editor.Session.Widget(P("widgets[2]"))!.LocalBounds.Left);
    });

    [Fact]
    public void Selecting_from_layers_enters_the_group_and_drag_moves_only_the_child() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Zoom();
        var child = P("widgets[2].children[0]");
        editor.Model.Layers.Click(editor.Model.Layers.Find(child)!, ModifierKeys.None);
        Assert.Equal(P("widgets[2]"), editor.Canvas.Group);
        editor.Canvas.PointerDown(editor.View(1035, 135), ModifierKeys.Alt, 1);
        editor.Canvas.PointerMove(editor.View(1055, 155), ModifierKeys.Alt);
        editor.Canvas.PointerUp(editor.View(1055, 155), ModifierKeys.Alt);
        Assert.Equal([child], editor.Session.Selection);
        Assert.Equal(30, editor.Session.Widget(child)!.LocalBounds.Left);
        Assert.Equal(1000, editor.Session.Widget(P("widgets[2]"))!.LocalBounds.Left);
    });

    [Theory]
    [InlineData(ModifierKeys.Control, 1)]
    [InlineData(ModifierKeys.None, 2)]
    public void Pick_inside_selects_the_child(ModifierKeys keys, int clicks) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Zoom();
        editor.Canvas.PointerDown(editor.View(1035, 135), keys, clicks);
        editor.Canvas.PointerUp(editor.View(1035, 135), keys);
        Assert.Equal([P("widgets[2].children[0]")], editor.Session.Selection);
        Assert.Equal(P("widgets[2]"), editor.Canvas.Group);
    });

    [Fact]
    public void Plain_click_selects_the_group_and_explains_how_to_pick_inside() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Zoom();
        editor.Canvas.PointerDown(editor.View(1035, 135), ModifierKeys.None, 1);
        editor.Canvas.PointerUp(editor.View(1035, 135), ModifierKeys.None);
        Assert.Equal([P("widgets[2]")], editor.Session.Selection);
        Assert.Equal("Double-click, or Ctrl+click, to pick something inside this group.", editor.Model.Message);
    });
}
