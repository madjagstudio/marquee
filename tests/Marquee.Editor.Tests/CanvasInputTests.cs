using System.Windows;
using System.Windows.Input;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;
using Marquee.Editor.Session;
using Marquee.Editor.Tests.TestSupport;
using SkiaSharp;
using static Marquee.Editor.Tests.TestSupport.OpenEditor;

namespace Marquee.Editor.Tests;

/// The canvas's mouse and keys, driven through the view-model as the view drives it: positions in the view's units,
/// at 100% on a screen at 150% (OpenEditor.Zoom), so each one goes through the zoom and the DPI to theme pixels.
public class CanvasInputTests
{
    [Fact]
    public void A_drag_previews_the_move_and_letting_go_writes_one_undo_step() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Zoom();
        var canvas = editor.Canvas;
        var session = editor.Session;

        canvas.PointerDown(editor.View(200, 150), ModifierKeys.None, 1);
        Assert.Equal([P("widgets[0]")], session.Selection);
        // Nothing is dragged until the mouse has gone CanvasDrag.StartDistance.
        Assert.False(session.IsPreviewing);

        // 40 by 20 units at 2/3 of a unit per theme pixel: 60 by 30 theme pixels. Alt keeps snapping out of it.
        canvas.PointerMove(editor.View(200, 150) + new Vector(20, 10), ModifierKeys.Alt);
        canvas.PointerMove(editor.View(200, 150) + new Vector(40, 20), ModifierKeys.Alt);

        Assert.True(session.IsPreviewing);
        Assert.Equal([new WidgetPlacement(P("widgets[0]"), SKRect.Create(160, 130, 200, 100), 0)], session.PreviewPlacements);
        Assert.Equal(TestEnvironment.Layout, session.Document.Text);
        // The outline follows the drag.
        Assert.Equal(editor.View(160, 130), canvas.Overlay.Outlines[0][0]);

        canvas.PointerUp(editor.View(200, 150) + new Vector(40, 20), ModifierKeys.Alt);

        Assert.False(session.IsPreviewing);
        Assert.Equal(1, session.History.Count);
        Assert.Equal("Move", session.History.NextUndo!.Label);
        Assert.Contains("at: [160, 130], size: [200, 100]", session.Document.Text);
    });

    [Fact]
    public void A_click_without_moving_selects_and_writes_nothing() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Zoom();

        editor.Canvas.PointerDown(editor.View(550, 150), ModifierKeys.None, 1);
        editor.Canvas.PointerMove(editor.View(550, 150) + new Vector(1, 1), ModifierKeys.None);
        editor.Canvas.PointerUp(editor.View(550, 150) + new Vector(1, 1), ModifierKeys.None);

        Assert.Equal([P("widgets[1]")], editor.Session.Selection);
        Assert.False(editor.Session.CanUndo);
    });

    [Fact]
    public void Esc_during_a_drag_puts_it_back() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Zoom();

        editor.Canvas.PointerDown(editor.View(200, 150), ModifierKeys.None, 1);
        editor.Canvas.PointerMove(editor.View(260, 150), ModifierKeys.None);
        Assert.True(editor.Model.KeyDown(Key.Escape, ModifierKeys.None));
        editor.Canvas.PointerUp(editor.View(260, 150), ModifierKeys.None);

        Assert.False(editor.Session.IsPreviewing);
        Assert.False(editor.Session.CanUndo);
        Assert.Equal(TestEnvironment.Layout, editor.Session.Document.Text);
    });

    [Fact]
    public void Dragging_a_handle_resizes_and_shift_keeps_the_proportions() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Zoom();
        editor.Session.Select([P("widgets[0]")]);
        var corner = editor.Canvas.Overlay.Handles[4].At;  // bottom-right, after the top-left, top, top-right and right

        Assert.Equal(editor.View(300, 200).X, corner.X, 3);
        Assert.Equal(editor.View(300, 200).Y, corner.Y, 3);
        editor.Canvas.PointerDown(corner, ModifierKeys.None, 1);
        editor.Canvas.PointerMove(editor.View(500, 220), ModifierKeys.Shift);
        editor.Canvas.PointerUp(editor.View(500, 220), ModifierKeys.Shift);

        Assert.Contains("at: [100, 100], size: [400, 200]", editor.Session.Document.Text);
        Assert.Equal("Resize", editor.Session.History.NextUndo!.Label);
    });

    [Fact]
    public void A_box_drawn_on_empty_canvas_selects_what_it_holds_and_shift_click_adds_or_removes() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Zoom();
        var canvas = editor.Canvas;

        canvas.PointerDown(editor.View(50, 50), ModifierKeys.None, 1);
        canvas.PointerMove(editor.View(700, 300), ModifierKeys.None);
        Assert.NotNull(canvas.Overlay.Marquee);
        canvas.PointerUp(editor.View(700, 300), ModifierKeys.None);

        Assert.Equal([P("widgets[0]"), P("widgets[1]")], editor.Session.Selection);
        Assert.Null(canvas.Overlay.Marquee);

        canvas.PointerDown(editor.View(550, 150), ModifierKeys.Shift, 1);
        canvas.PointerUp(editor.View(550, 150), ModifierKeys.Shift);
        Assert.Equal([P("widgets[0]")], editor.Session.Selection);

        canvas.PointerDown(editor.View(1100, 200), ModifierKeys.Control, 1);
        canvas.PointerUp(editor.View(1100, 200), ModifierKeys.Control);
        Assert.Equal([P("widgets[0]"), P("widgets[2]")], editor.Session.Selection);

        Assert.True(editor.Model.KeyDown(Key.Escape, ModifierKeys.None));
        Assert.Empty(editor.Session.Selection);
    });

    [Fact]
    public void Double_click_works_inside_a_group_and_esc_leaves_it() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Zoom();
        var canvas = editor.Canvas;

        canvas.PointerDown(editor.View(1030, 130), ModifierKeys.None, 1);
        canvas.PointerUp(editor.View(1030, 130), ModifierKeys.None);
        Assert.Equal([P("widgets[2]")], editor.Session.Selection);
        canvas.PointerDown(editor.View(1030, 130), ModifierKeys.None, 2);
        canvas.PointerUp(editor.View(1030, 130), ModifierKeys.None);

        Assert.Equal(P("widgets[2]"), canvas.Group);
        Assert.NotNull(canvas.Overlay.Group);
        canvas.PointerDown(editor.View(1030, 130), ModifierKeys.None, 1);
        canvas.PointerUp(editor.View(1030, 130), ModifierKeys.None);
        Assert.Equal([P("widgets[2].children[0]")], editor.Session.Selection);

        editor.Model.KeyDown(Key.Escape, ModifierKeys.None);
        Assert.Null(canvas.Group);
        Assert.Equal([P("widgets[2]")], editor.Session.Selection);
    });

    [Fact]
    public void Arrow_keys_nudge_by_one_and_shift_by_ten() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Session.Select([P("widgets[1]")]);

        Assert.True(editor.Model.KeyDown(Key.Right, ModifierKeys.None));
        Assert.True(editor.Model.KeyDown(Key.Down, ModifierKeys.Shift));

        Assert.Contains("at: [501, 110]", editor.Session.Document.Text);
    });

    [Fact]
    public void Delete_then_ctrl_z_and_ctrl_y() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Session.Select([P("widgets[0]")]);

        editor.Model.KeyDown(Key.Delete, ModifierKeys.None);
        Assert.DoesNotContain("#FF0000", editor.Session.Document.Text);

        editor.Model.KeyDown(Key.Z, ModifierKeys.Control);
        Assert.Equal(TestEnvironment.Layout, editor.Session.Document.Text);

        editor.Model.KeyDown(Key.Y, ModifierKeys.Control);
        Assert.DoesNotContain("#FF0000", editor.Session.Document.Text);
    });

    [Fact]
    public void Copy_paste_duplicate_group_and_ungroup_from_the_keyboard() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var session = editor.Session;
        session.Select([P("widgets[1]")]);

        editor.Model.KeyDown(Key.C, ModifierKeys.Control);
        Assert.StartsWith("# Marquee widgets", environment.Clipboard.Text);
        editor.Model.KeyDown(Key.V, ModifierKeys.Control);
        Assert.Equal(4, session.Widgets.Count);
        Assert.Contains("at: [520, 120]", session.Document.Text);

        editor.Model.KeyDown(Key.D, ModifierKeys.Control);
        Assert.Equal(5, session.Widgets.Count);

        session.Select([P("widgets[0]"), P("widgets[1]")]);
        editor.Model.KeyDown(Key.G, ModifierKeys.Control);
        Assert.Equal(4, session.Widgets.Count);
        Assert.True(session.Widget(session.Selection[0])!.IsGroup);

        editor.Model.KeyDown(Key.G, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal(5, session.Widgets.Count);
    });

    [Fact]
    public void Paste_goes_into_the_group_being_worked_in() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Zoom();
        environment.Clipboard.Text = "- { type: shape, shape: rect, at: [0, 0], size: [10, 10] }\n";
        editor.Canvas.PointerDown(editor.View(1200, 300), ModifierKeys.None, 2);

        editor.Model.KeyDown(Key.V, ModifierKeys.Control);

        Assert.Equal([P("widgets[2].children[1]")], editor.Session.Selection);
    });

    [Fact]
    public void A_read_only_theme_selects_but_never_changes() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var example = environment.AddTheme("example-landscape", installed: true);
        environment.Dialogs.CopyAnswer = CopyChoice.ViewOnly;
        using var editor = new OpenEditor(environment, example);
        editor.Zoom();

        editor.Canvas.PointerDown(editor.View(200, 150), ModifierKeys.None, 1);
        editor.Canvas.PointerMove(editor.View(300, 150), ModifierKeys.None);
        editor.Canvas.PointerUp(editor.View(300, 150), ModifierKeys.None);
        editor.Model.KeyDown(Key.Delete, ModifierKeys.None);

        Assert.True(editor.Model.IsReadOnly);
        Assert.False(editor.Canvas.CanEdit);
        Assert.Equal([P("widgets[0]")], editor.Session.Selection);
        Assert.Equal(TestEnvironment.Layout, editor.Session.Document.Text);
        Assert.Equal(Strings.ReadOnlyRefused, editor.Model.Message);
        Assert.Equal(Strings.SaveReadOnly, editor.Model.SaveText);
    });
}
