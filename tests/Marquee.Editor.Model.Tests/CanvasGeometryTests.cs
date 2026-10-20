using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// What the mouse finds on the canvas: the front-most widget it can grab at a point, the widgets a selection box takes,
/// and the group being worked in, all in theme pixels, with rotated widgets and groups tested as they are drawn.
public class CanvasGeometryTests
{
    /// The group turns 90° about its centre (1200, 200), so on the canvas it covers x 1100–1300 and y 0–400; the last
    /// rect turns about (700, 210), covering x 690–710 and y 110–310.
    internal const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: shape, shape: rect, at: [0, 0], size: [1920, 480], name: Backdrop }\n" +
        "  - { type: shape, shape: rect, at: [100, 100], size: [200, 100] }\n" +
        "  - { type: shape, shape: rect, at: [150, 120], size: [100, 40], hidden: true }\n" +
        "  - { type: shape, shape: rect, at: [120, 110], size: [40, 40], locked: true }\n" +
        "  - type: group\n" +
        "    at: [1000, 100]\n" +
        "    size: [400, 200]\n" +
        "    rotate: 90\n" +
        "    children:\n" +
        "      - { type: shape, shape: rect, at: [0, 0], size: [100, 20] }\n" +
        "      - { type: shape, shape: rect, at: [300, 0], size: [100, 200] }\n" +
        "  - { type: shape, shape: rect, at: [600, 200], size: [200, 20], rotate: 90 }\n";

    static YamlPath P(string path) => YamlPath.Parse(path);

    static (TempTheme Folder, EditorSession Session, CanvasGeometry Geometry) Open(string layout = Layout)
    {
        var folder = TempTheme.With(layout);
        var session = folder.Open();
        return (folder, session, CanvasGeometry.Of(session));
    }

    [Theory]
    [InlineData(50, 50, "widgets[0]")]
    [InlineData(110, 105, "widgets[1]")]
    [InlineData(200, 140, "widgets[1]")] // the hidden rect in front isn't there to click
    [InlineData(130, 130, "widgets[1]")] // nor is the locked one
    [InlineData(700, 150, "widgets[5]")] // inside the turned rect
    [InlineData(620, 210, "widgets[0]")] // inside its box as written, which it no longer covers
    [InlineData(1200, 20, "widgets[4]")] // the turned group, as a whole
    [InlineData(1050, 200, "widgets[0]")]
    public void The_front_most_widget_the_mouse_can_grab_is_hit(float x, float y, string expected)
    {
        var (folder, session, geometry) = Open();
        using var _ = folder;
        using var __ = session;

        Assert.Equal(P(expected), geometry.HitTest(new SKPoint(x, y))?.Path);
    }

    [Fact]
    public void Nothing_is_hit_off_the_widgets()
    {
        var (folder, session, geometry) = Open(
            "canvas: { orientation: landscape }\nwidgets:\n  - { type: shape, shape: rect, at: [100, 100], size: [200, 100] }\n");
        using var _ = folder;
        using var __ = session;

        Assert.Null(geometry.HitTest(new SKPoint(99, 150)));
        Assert.Equal(P("widgets[0]"), geometry.HitTest(new SKPoint(100, 100))?.Path);
        Assert.Equal(P("widgets[0]"), geometry.HitTest(new SKPoint(300, 200))?.Path);
    }

    [Fact]
    public void Inside_a_group_its_children_are_hit_in_its_turned_frame()
    {
        var (folder, session, geometry) = Open();
        using var _ = folder;
        using var __ = session;
        var group = P("widgets[4]");

        // The first child's point (50, 10) is (1050, 110) before the group turns, and (1290, 50) after.
        Assert.Equal(new SKPoint(1290, 50), Round(geometry.ToCanvas(new SKPoint(50, 10), group)));
        Assert.Equal(new SKPoint(50, 10), Round(geometry.ToLocal(new SKPoint(1290, 50), group)));
        Assert.Equal(P("widgets[4].children[0]"), geometry.HitTest(new SKPoint(1290, 50), group)?.Path);
        Assert.Equal(P("widgets[4].children[1]"), geometry.HitTest(new SKPoint(1200, 370), group)?.Path);
        // The group's box, with no child there, and outside it.
        Assert.Null(geometry.HitTest(new SKPoint(1200, 200), group));
        Assert.Null(geometry.HitTest(new SKPoint(50, 50), group));
    }

    [Fact]
    public void A_child_is_clipped_to_its_group_and_hidden_or_locked_with_it()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - type: group\n" +
            "    at: [100, 100]\n" +
            "    size: [100, 100]\n" +
            "    children:\n" +
            "      - { type: shape, shape: rect, at: [50, 50], size: [200, 200] }\n" +
            "  - type: group\n" +
            "    at: [400, 100]\n" +
            "    size: [100, 100]\n" +
            "    locked: true\n" +
            "    children:\n" +
            "      - { type: shape, shape: rect, at: [0, 0], size: [100, 100] }\n" +
            "  - type: group\n" +
            "    at: [700, 100]\n" +
            "    size: [100, 100]\n" +
            "    hidden: true\n" +
            "    children:\n" +
            "      - { type: shape, shape: rect, at: [0, 0], size: [100, 100] }\n";
        var (folder, session, geometry) = Open(layout);
        using var _ = folder;
        using var __ = session;

        Assert.Equal(P("widgets[0].children[0]"), geometry.HitTest(new SKPoint(190, 190), P("widgets[0]"))?.Path);
        Assert.Null(geometry.HitTest(new SKPoint(240, 240), P("widgets[0]")));
        Assert.Null(geometry.HitTest(new SKPoint(450, 150), P("widgets[1]")));
        Assert.Null(geometry.HitTest(new SKPoint(750, 150), P("widgets[2]")));
        Assert.True(geometry.IsLocked(P("widgets[1].children[0]")));
        Assert.True(geometry.IsHidden(P("widgets[2].children[0]")));
        Assert.False(geometry.IsLocked(P("widgets[0].children[0]")));
    }

    [Fact]
    public void A_thin_line_is_hit_within_the_slop()
    {
        var (folder, session, geometry) = Open(
            "canvas: { orientation: landscape }\nwidgets:\n" +
            "  - { type: shape, shape: line, at: [100, 100], size: [200, 0], style: { stroke: { color: \"#FFFFFF\" } } }\n");
        using var _ = folder;
        using var __ = session;

        Assert.Null(geometry.HitTest(new SKPoint(150, 102)));
        Assert.Equal(P("widgets[0]"), geometry.HitTest(new SKPoint(150, 102), slop: 3)?.Path);
    }

    [Fact]
    public void A_selection_box_takes_the_widgets_wholly_inside_it_that_the_mouse_can_grab()
    {
        var (folder, session, geometry) = Open();
        using var _ = folder;
        using var __ = session;

        Assert.Equal([P("widgets[1]")], geometry.InBox(new SKRect(90, 90, 310, 210)).Select(w => w.Path));
        Assert.Empty(geometry.InBox(new SKRect(90, 90, 290, 210)));
        // The turned rect is taken by the box it covers on screen, not the one written.
        Assert.Equal([P("widgets[5]")], geometry.InBox(new SKRect(680, 100, 720, 320)).Select(w => w.Path));
        Assert.Empty(geometry.InBox(new SKRect(590, 190, 810, 230)));
        // Inside a group, its children; a box drawn backwards counts the same.
        Assert.Equal([P("widgets[4].children[0]")],
            geometry.InBox(new SKRect(1310, 0, 1260, 110), P("widgets[4]")).Select(w => w.Path));
    }

    [Fact]
    public void A_widgets_box_on_the_canvas_turns_with_it_and_its_groups()
    {
        var (folder, session, geometry) = Open();
        using var _ = folder;
        using var __ = session;

        Assert.Equal(new SKRect(100, 100, 300, 200), geometry.BoundsOf(P("widgets[1]")));
        Assert.Equal(new SKRect(690, 110, 710, 310), Round(geometry.BoundsOf(P("widgets[5]"))));
        Assert.Equal(new SKRect(1280, 0, 1300, 100), Round(geometry.BoundsOf(P("widgets[4].children[0]"))));
        Assert.Equal(90, geometry.TurnOf(P("widgets[4].children[0]")));
        Assert.Equal(
            [new SKPoint(710, 110), new SKPoint(710, 310), new SKPoint(690, 310), new SKPoint(690, 110)],
            geometry.CornersOf(P("widgets[5]")).Select(Round));
    }

    [Fact]
    public void Double_clicking_a_group_works_inside_it_and_a_click_outside_or_esc_leaves()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - type: group\n" +
            "    at: [100, 100]\n" +
            "    size: [400, 300]\n" +
            "    children:\n" +
            "      - { type: shape, shape: rect, at: [10, 10], size: [50, 50] }\n" +
            "      - type: group\n" +
            "        at: [100, 100]\n" +
            "        size: [200, 100]\n" +
            "        children:\n" +
            "          - { type: shape, shape: rect, at: [20, 20], size: [30, 30] }\n" +
            "  - { type: shape, shape: rect, at: [600, 100], size: [50, 50] }\n";
        var (folder, session, geometry) = Open(layout);
        using var _ = folder;
        using var __ = session;

        var outer = geometry.GroupToEnter(new SKPoint(150, 150), null);
        Assert.Equal(P("widgets[0]"), outer);
        Assert.Null(geometry.GroupToEnter(new SKPoint(620, 120), null));
        var inner = geometry.GroupToEnter(new SKPoint(250, 250), outer);
        Assert.Equal(P("widgets[0].children[1]"), inner);
        // Positions inside are the group's, as the YAML writes them.
        Assert.Equal(new SKPoint(20, 20), geometry.ToLocal(new SKPoint(220, 220), inner));

        Assert.Equal(inner, geometry.GroupAfterClick(new SKPoint(250, 250), inner));
        Assert.Equal(outer, geometry.GroupAfterClick(new SKPoint(150, 150), inner));
        Assert.Null(geometry.GroupAfterClick(new SKPoint(620, 120), inner));
        Assert.Equal(outer, CanvasGeometry.GroupOf(inner!));
        Assert.Null(CanvasGeometry.GroupOf(outer!));
        Assert.Equal(
            [P("widgets[0].children[0]"), P("widgets[0].children[1]")], geometry.WidgetsIn(outer).Select(w => w.Path));
    }

    static SKPoint Round(SKPoint point) => new(MathF.Round(point.X, 3), MathF.Round(point.Y, 3));

    static SKRect Round(SKRect rect) =>
        new(MathF.Round(rect.Left, 3), MathF.Round(rect.Top, 3), MathF.Round(rect.Right, 3), MathF.Round(rect.Bottom, 3));
}
