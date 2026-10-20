using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// Dragging on the canvas: moving, resizing by a handle and turning by the rotate handle, worked out in theme pixels
/// from the mouse and shown through the session's preview, then written as one undo step when let go.
public class CanvasDragTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: shape, shape: rect, at: [100, 100], size: [200, 100] }\n" +
        "  - { type: shape, shape: rect, at: [600, 200], size: [200, 20], rotate: 90 }\n" +
        "  - { type: shape, shape: line, at: [1000, 100], size: [100, 50], style: { stroke: { color: \"#FFFFFF\" } } }\n" +
        "  - { type: shape, shape: rect, at: [1300, 300], size: [40, 40], locked: true }\n" +
        "  - type: group\n" +
        "    at: [1500, 100]\n" +
        "    size: [300, 300]\n" +
        "    rotate: 90\n" +
        "    children:\n" +
        "      - { type: shape, shape: rect, at: [0, 0], size: [50, 50] }\n";

    static YamlPath P(string path) => YamlPath.Parse(path);

    static SelectionFrame Frame(EditorSession session, params string[] paths) =>
        SelectionFrame.Of(CanvasGeometry.Of(session), paths.Select(P).ToList())!;

    [Fact]
    public void A_move_previews_then_writes_whole_pixels_once()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var drag = CanvasDrag.Move(session, [P("widgets[0]")], new SKPoint(150, 150))!;
        drag.Update(new SKPoint(170.4f, 161), DragKeys.Alt, zoom: 1);
        drag.Update(new SKPoint(187.4f, 162), DragKeys.Alt, zoom: 1);

        Assert.True(session.IsPreviewing);
        Assert.Equal(Layout, session.Document.Text);
        drag.Commit();
        Assert.Equal(Layout.Replace("at: [100, 100], size: [200, 100]", "at: [137, 112], size: [200, 100]"), session.Document.Text);
        Assert.Equal("Move", session.History.NextUndo!.Label);
        Assert.Equal(1, session.History.Count);
    }

    [Fact]
    public void A_move_snaps_unless_alt_is_held_and_says_which_guides_to_draw()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        // Its left edge 3 px from the canvas's centre line.
        var drag = CanvasDrag.Move(session, [P("widgets[0]")], new SKPoint(150, 150))!;
        var guides = drag.Update(new SKPoint(1013, 150), DragKeys.None, zoom: 1);
        Assert.Contains(new SnapGuide(new SKPoint(960, 0), new SKPoint(960, 480)), guides);
        Assert.Empty(drag.Update(new SKPoint(1013, 150), DragKeys.Alt, zoom: 1));
        drag.Update(new SKPoint(1013, 150), DragKeys.None, zoom: 1);
        drag.Commit();

        Assert.Equal(Layout.Replace("at: [100, 100], size: [200, 100]", "at: [960, 100], size: [200, 100]"), session.Document.Text);
    }

    /// Positions are whole pixels, so a snap lands exactly on its guide: a 100 × 50 box's centre on the canvas's, from a
    /// pointer between pixels at half zoom; a 101 × 51 box's centre can't be on 960 or 240, so that line isn't offered.
    [Theory]
    [InlineData(100, 50, "at: [910, 215]", 2)]
    [InlineData(101, 51, "at: [912, 216]", 0)]
    public void A_snapped_move_is_written_on_its_guides(int width, int height, string written, int guides)
    {
        var layout = $"canvas: {{ orientation: landscape }}\nwidgets:\n  - {{ type: shape, shape: rect, at: [100, 100], size: [{width}, {height}] }}\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();

        // The box's centre dropped about 2 px right of and 1 px below the canvas's.
        var drag = CanvasDrag.Move(session, [P("widgets[0]")], new SKPoint(150, 125))!;
        var drawn = drag.Update(new SKPoint(150 + (960 - (100 + width / 2f)) + 2.3f, 125 + (240 - (100 + height / 2f)) + 1.2f), DragKeys.None, 0.5f);
        drag.Commit();

        Assert.Equal(guides, drawn.Count);
        Assert.Contains(written, session.Document.Text);
        var box = session.Widget(P("widgets[0]"))!.Bounds;
        foreach (var guide in drawn)
            Assert.True(guide.From.X == guide.To.X ? guide.From.X == box.MidX : guide.From.Y == box.MidY, guide.ToString());
    }

    [Fact]
    public void A_multi_selection_moves_together_and_snaps_by_the_box_round_it()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - { type: shape, shape: rect, at: [100, 100], size: [100, 100] }\n" +
            "  - { type: shape, shape: rect, at: [300, 150], size: [100, 100] }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();

        // The pair spans x 100–400: moved 556 px its right edge would be at 956, 4 px from the canvas's centre.
        var drag = CanvasDrag.Move(session, [P("widgets[0]"), P("widgets[1]")], new SKPoint(0, 0))!;
        drag.Update(new SKPoint(556, 0), DragKeys.None, zoom: 1);
        drag.Commit();

        Assert.Equal(layout.Replace("at: [100, 100]", "at: [660, 100]").Replace("at: [300, 150]", "at: [860, 150]"),
            session.Document.Text);
        Assert.Equal(1, session.History.Count);
    }

    [Fact]
    public void A_child_of_a_turned_group_moves_the_way_the_mouse_does_on_screen()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        // The group is turned 90°: right on screen is up in the group's own coordinates.
        var drag = CanvasDrag.Move(session, [P("widgets[4].children[0]")], new SKPoint(0, 0))!;
        drag.Update(new SKPoint(30, 0), DragKeys.Alt, zoom: 1);
        drag.Commit();

        Assert.Equal(Layout.Replace("at: [0, 0], size: [50, 50]", "at: [0, -30], size: [50, 50]"), session.Document.Text);
    }

    [Fact]
    public void Locked_widgets_are_not_dragged()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        Assert.Null(CanvasDrag.Move(session, [P("widgets[3]")], new SKPoint(0, 0)));
        var drag = CanvasDrag.Move(session, [P("widgets[0]"), P("widgets[3]")], new SKPoint(0, 0))!;
        drag.Update(new SKPoint(10, 10), DragKeys.Alt, zoom: 1);
        drag.Commit();

        Assert.Equal(Layout.Replace("at: [100, 100]", "at: [110, 110]"), session.Document.Text);
    }

    [Fact]
    public void A_corner_resizes_from_the_opposite_corner()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var drag = CanvasDrag.Grab(session, Frame(session, "widgets[0]"), SelectionHandle.TopLeft, new SKPoint(102, 101))!;
        drag.Update(new SKPoint(52, 81), DragKeys.Alt, zoom: 1);
        drag.Commit();

        Assert.Equal(Layout.Replace("at: [100, 100], size: [200, 100]", "at: [50, 80], size: [250, 120]"), session.Document.Text);
        Assert.Equal("Resize", session.History.NextUndo!.Label);
    }

    [Fact]
    public void Shift_keeps_the_proportions()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var corner = CanvasDrag.Grab(session, Frame(session, "widgets[0]"), SelectionHandle.BottomRight, new SKPoint(300, 200))!;
        corner.Update(new SKPoint(350, 260), DragKeys.Shift, zoom: 1);
        corner.Commit();
        Assert.Equal(Layout.Replace("size: [200, 100]", "size: [320, 160]"), session.Document.Text);

        // An edge keeps the proportions about the box's middle line.
        var edge = CanvasDrag.Grab(session, Frame(session, "widgets[0]"), SelectionHandle.Right, new SKPoint(420, 180))!;
        edge.Update(new SKPoint(340, 180), DragKeys.Shift, zoom: 1);
        edge.Commit();
        Assert.Equal(Layout.Replace("at: [100, 100], size: [200, 100]", "at: [100, 120], size: [240, 120]"), session.Document.Text);
    }

    [Fact]
    public void A_size_never_goes_below_1_px_or_the_catalogs_min_for_the_type()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        // Dragged past the opposite edge, the left edge stops 1 px short of it.
        var rect = CanvasDrag.Grab(session, Frame(session, "widgets[0]"), SelectionHandle.Left, new SKPoint(100, 150))!;
        rect.Update(new SKPoint(500, 150), DragKeys.Alt, zoom: 1);
        rect.Commit();
        Assert.Contains("at: [299, 100], size: [1, 100]", session.Document.Text);

        // A line's catalog minimum is 0 (MinFor), so it can be made flat.
        var line = CanvasDrag.Grab(session, Frame(session, "widgets[2]"), SelectionHandle.Bottom, new SKPoint(1050, 150))!;
        line.Update(new SKPoint(1050, 20), DragKeys.Alt, zoom: 1);
        line.Commit();
        Assert.Contains("at: [1000, 100], size: [100, 0]", session.Document.Text);
    }

    [Fact]
    public void A_turned_widget_resizes_along_its_own_sides_keeping_the_opposite_side_in_place()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        // Turned 90° about (700, 210), its right edge is at the bottom on screen, at y 310: drag it 50 px down.
        var drag = CanvasDrag.Grab(session, Frame(session, "widgets[1]"), SelectionHandle.Right, new SKPoint(700, 310))!;
        drag.Update(new SKPoint(700, 360), DragKeys.None, zoom: 1);
        drag.Commit();

        Assert.Contains("at: [575, 225], size: [250, 20], rotate: 90", session.Document.Text);
        // Its left edge, now at the top on screen, hasn't moved.
        Assert.Equal(110, CanvasGeometry.Of(session).BoundsOf(P("widgets[1]")).Top, 3);
    }

    [Fact]
    public void Several_siblings_resize_together_within_the_box_round_them()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - { type: shape, shape: rect, at: [100, 100], size: [100, 100] }\n" +
            "  - { type: shape, shape: rect, at: [300, 100], size: [100, 100] }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();

        var frame = Frame(session, "widgets[0]", "widgets[1]");
        var drag = CanvasDrag.Grab(session, frame, SelectionHandle.Right, new SKPoint(400, 150))!;
        drag.Update(new SKPoint(700, 150), DragKeys.Alt, zoom: 1);
        drag.Commit();

        Assert.Equal(layout.Replace("at: [100, 100], size: [100, 100]", "at: [100, 100], size: [200, 100]")
            .Replace("at: [300, 100], size: [100, 100]", "at: [500, 100], size: [200, 100]"), session.Document.Text);
    }

    [Fact]
    public void The_rotate_handle_turns_about_the_centre_and_shift_snaps_to_15_degrees()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        // The centre is (200, 150); the handle starts straight above it.
        var start = new SKPoint(200, 50);

        var drag = CanvasDrag.Grab(session, Frame(session, "widgets[0]"), SelectionHandle.Rotate, start)!;
        drag.Update(new SKPoint(300, 150), DragKeys.None, zoom: 1);
        drag.Commit();
        Assert.Contains("size: [200, 100], rotate: 90 }", session.Document.Text);
        Assert.Equal("Rotate", session.History.NextUndo!.Label);
        session.Undo();

        // 50° clockwise of straight up, which Shift takes to 45°, and without it to the whole degree.
        var at50 = new SKPoint(200 + 100 * MathF.Sin(50 * MathF.PI / 180), 150 - 100 * MathF.Cos(50 * MathF.PI / 180));
        var snapped = CanvasDrag.Grab(session, Frame(session, "widgets[0]"), SelectionHandle.Rotate, start)!;
        snapped.Update(at50, DragKeys.Shift, zoom: 1);
        snapped.Commit();
        Assert.Contains("rotate: 45 }", session.Document.Text);
        session.Undo();

        var free = CanvasDrag.Grab(session, Frame(session, "widgets[0]"), SelectionHandle.Rotate, start)!;
        free.Update(at50, DragKeys.None, zoom: 1);
        free.Commit();
        Assert.Contains("rotate: 50 }", session.Document.Text);
    }

    /// A handle pressed and let go, or nudged less than StartDistance on screen, changes nothing: a fractional or turned
    /// box isn't rounded to whole pixels as an undo step.
    [Theory]
    [InlineData(SelectionHandle.TopLeft)]
    [InlineData(SelectionHandle.Right)]
    [InlineData(SelectionHandle.Rotate)]
    public void A_handle_let_go_where_it_was_grabbed_writes_nothing(SelectionHandle handle)
    {
        const string layout = "canvas: { orientation: landscape }\nwidgets:\n  - { type: shape, shape: rect, at: [100.5, 100.5], size: [40.5, 40], rotate: 30.5 }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();
        var frame = Frame(session, "widgets[0]");
        var at = frame.Handles(2).Single(h => h.Handle == handle).At;
        var start = new SKPoint(at.X / 2, at.Y / 2);

        var drag = CanvasDrag.Grab(session, frame, handle, start);
        drag.Update(start, DragKeys.None, 2);
        drag.Update(start + new SKPoint(0.5f, -0.5f), DragKeys.None, 2);
        drag.Commit();

        Assert.Equal(layout, session.Document.Text);
        Assert.Equal(0, session.History.Count);

        // Once it has gone further, it resizes or turns as usual, even back where it started.
        var moved = CanvasDrag.Grab(session, frame, handle, start);
        moved.Update(start + new SKPoint(20, 20), DragKeys.None, 2);
        moved.Update(start + new SKPoint(0.5f, 0), DragKeys.None, 2);
        moved.Commit();
        Assert.Equal(1, session.History.Count);
    }

    [Fact]
    public void Cancel_puts_everything_back()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var drag = CanvasDrag.Move(session, [P("widgets[0]")], new SKPoint(150, 150))!;
        drag.Update(new SKPoint(250, 250), DragKeys.None, zoom: 1);
        drag.Cancel();

        Assert.False(session.IsPreviewing);
        Assert.Equal(Layout, session.Document.Text);
        Assert.False(session.CanUndo);
    }
}
