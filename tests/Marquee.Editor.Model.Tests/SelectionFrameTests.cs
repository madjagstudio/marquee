using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// The selection's handles: eight to resize and one above the box to rotate, placed in screen pixels for the zoom, so
/// they stay the same size on screen, and turned with the widget.
public class SelectionFrameTests
{
    static YamlPath P(string path) => YamlPath.Parse(path);

    static SelectionFrame Frame(CanvasGeometry geometry, params string[] paths) =>
        SelectionFrame.Of(geometry, paths.Select(P).ToList()) ?? throw new InvalidOperationException("No frame.");

    static SKPoint At(SelectionFrame frame, SelectionHandle handle, float zoom) =>
        Round(frame.Handles(zoom).Single(h => h.Handle == handle).At);

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Handles_sit_on_the_box_in_screen_pixels(float zoom)
    {
        using var folder = TempTheme.With(CanvasGeometryTests.Layout);
        using var session = folder.Open();
        var frame = Frame(CanvasGeometry.Of(session), "widgets[1]");

        Assert.Equal(9, frame.Handles(zoom).Count);
        Assert.Equal(new SKPoint(100 * zoom, 100 * zoom), At(frame, SelectionHandle.TopLeft, zoom));
        Assert.Equal(new SKPoint(200 * zoom, 100 * zoom), At(frame, SelectionHandle.Top, zoom));
        Assert.Equal(new SKPoint(300 * zoom, 150 * zoom), At(frame, SelectionHandle.Right, zoom));
        Assert.Equal(new SKPoint(300 * zoom, 200 * zoom), At(frame, SelectionHandle.BottomRight, zoom));
        Assert.Equal(new SKPoint(100 * zoom, 200 * zoom), At(frame, SelectionHandle.BottomLeft, zoom));
        // The rotate handle is the same distance above the box on screen at any zoom.
        Assert.Equal(new SKPoint(200 * zoom, 100 * zoom - SelectionFrame.RotateHandleGap), At(frame, SelectionHandle.Rotate, zoom));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(2f)]
    public void A_handle_is_hit_within_a_few_screen_pixels(float zoom)
    {
        using var folder = TempTheme.With(CanvasGeometryTests.Layout);
        using var session = folder.Open();
        var frame = Frame(CanvasGeometry.Of(session), "widgets[1]");
        var near = SelectionFrame.HandleRadius - 1;

        Assert.Equal(SelectionHandle.TopLeft, frame.HitHandle(new SKPoint(100 * zoom + near, 100 * zoom - near), zoom));
        Assert.Equal(SelectionHandle.Bottom, frame.HitHandle(new SKPoint(200 * zoom, 200 * zoom + near), zoom));
        Assert.Equal(SelectionHandle.Rotate,
            frame.HitHandle(new SKPoint(200 * zoom, 100 * zoom - SelectionFrame.RotateHandleGap + near), zoom));
        Assert.Null(frame.HitHandle(new SKPoint(150 * zoom, 150 * zoom), zoom));
        Assert.Null(frame.HitHandle(new SKPoint(100 * zoom + SelectionFrame.HandleRadius + 2, 100 * zoom), zoom));
    }

    [Fact]
    public void Handles_turn_with_the_widget()
    {
        using var folder = TempTheme.With(CanvasGeometryTests.Layout);
        using var session = folder.Open();
        // Turned 90° about (700, 210): its top edge is now on the right.
        var frame = Frame(CanvasGeometry.Of(session), "widgets[5]");

        Assert.Equal(new SKPoint(710, 110), At(frame, SelectionHandle.TopLeft, 1));
        Assert.Equal(new SKPoint(710, 210), At(frame, SelectionHandle.Top, 1));
        Assert.Equal(new SKPoint(710 + SelectionFrame.RotateHandleGap, 210), At(frame, SelectionHandle.Rotate, 1));
        Assert.Equal(new SKPoint(1400, 620), At(frame, SelectionHandle.Right, 2));
        Assert.Equal(SelectionHandle.Right, frame.HitHandle(new SKPoint(1402, 618), 2));
    }

    [Fact]
    public void Siblings_share_one_upright_frame_without_a_rotate_handle()
    {
        using var folder = TempTheme.With(CanvasGeometryTests.Layout);
        using var session = folder.Open();
        var geometry = CanvasGeometry.Of(session);

        var frame = Frame(geometry, "widgets[1]", "widgets[5]");

        Assert.Equal(new SKRect(100, 100, 710, 310), Round(frame.Box));
        Assert.Equal(8, frame.Handles(1).Count);
        Assert.DoesNotContain(frame.Handles(1), h => h.Handle == SelectionHandle.Rotate);
        // Locked widgets and those in different lists have no handles to drag.
        Assert.Null(SelectionFrame.Of(geometry, [P("widgets[3]")]));
        Assert.Null(SelectionFrame.Of(geometry, [P("widgets[1]"), P("widgets[4].children[0]")]));
        Assert.Equal([P("widgets[1]")], Frame(geometry, "widgets[1]", "widgets[3]").Widgets.Select(w => w.Path));
    }

    const string Small =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: shape, shape: rect, at: [100, 100], size: [12, 12], style: { fill: \"#FF0000\" } }\n" +
        "  - { type: shape, shape: rect, at: [300, 100], size: [4, 40], style: { fill: \"#FF0000\" } }\n" +
        "  - { type: shape, shape: rect, at: [500, 100], size: [12, 12], rotate: 45, style: { fill: \"#FF0000\" } }\n" +
        "  - { type: shape, shape: rect, at: [700, 100], size: [100, 100], style: { fill: \"#FF0000\" } }\n";

    /// At "fit" zoom a 12 px icon is 3 to 6 px on screen, smaller than its handles: a press inside it away from the
    /// handles' centres moves it, one right by a corner grabs that corner, and one outside grabs the nearest handle.
    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(4f)]
    public void A_press_inside_a_small_box_moves_it_and_one_by_a_handle_grabs_that_handle(float zoom)
    {
        using var folder = TempTheme.With(Small);
        using var session = folder.Open();
        var frame = Frame(CanvasGeometry.Of(session), "widgets[0]");

        Assert.Null(frame.HitHandle(new SKPoint(106 * zoom, 106 * zoom), zoom));
        Assert.Equal(SelectionHandle.BottomRight, frame.HitHandle(new SKPoint(112 * zoom - 0.5f, 112 * zoom - 0.5f), zoom));
        Assert.Equal(SelectionHandle.TopLeft, frame.HitHandle(new SKPoint(100 * zoom + 0.5f, 100 * zoom + 0.5f), zoom));
        Assert.Equal(SelectionHandle.BottomRight, frame.HitHandle(new SKPoint(112 * zoom + 3, 112 * zoom + 3), zoom));
        Assert.Equal(SelectionHandle.Right, frame.HitHandle(new SKPoint(112 * zoom + 4, 106 * zoom), zoom));
        Assert.Equal(SelectionHandle.Rotate, frame.HitHandle(new SKPoint(106 * zoom, 100 * zoom - SelectionFrame.RotateHandleGap), zoom));
    }

    [Fact]
    public void The_nearest_handle_in_reach_is_hit_not_the_first_corner()
    {
        using var folder = TempTheme.With(Small);
        using var session = folder.Open();
        var frame = Frame(CanvasGeometry.Of(session), "widgets[0]");

        // Top is 3.2 px away and TopRight 5.8: both in reach.
        Assert.Equal(SelectionHandle.Top, frame.HitHandle(new SKPoint(107, 97), 1));
        Assert.Equal(SelectionHandle.TopRight, frame.HitHandle(new SKPoint(110, 97), 1));
    }

    [Fact]
    public void A_thin_box_moves_from_its_middle_and_resizes_from_its_ends_and_sides()
    {
        using var folder = TempTheme.With(Small);
        using var session = folder.Open();
        var frame = Frame(CanvasGeometry.Of(session), "widgets[1]");

        Assert.Null(frame.HitHandle(new SKPoint(302, 120), 1));
        Assert.Equal(SelectionHandle.Top, frame.HitHandle(new SKPoint(302, 101), 1));
        Assert.Equal(SelectionHandle.Left, frame.HitHandle(new SKPoint(300.5f, 120), 1));
        Assert.Equal(SelectionHandle.Right, frame.HitHandle(new SKPoint(306, 120), 1));
    }

    [Fact]
    public void A_turned_small_box_moves_from_its_middle()
    {
        using var folder = TempTheme.With(Small);
        using var session = folder.Open();
        var frame = Frame(CanvasGeometry.Of(session), "widgets[2]");
        var corner = frame.Handles(1).Single(h => h.Handle == SelectionHandle.TopLeft).At;
        var centre = new SKPoint(506, 106);

        Assert.Null(frame.HitHandle(centre, 1));
        var inward = centre - corner;
        Assert.Equal(SelectionHandle.TopLeft, frame.HitHandle(corner + new SKPoint(inward.X / inward.Length * 0.5f, inward.Y / inward.Length * 0.5f), 1));
    }

    [Fact]
    public void A_large_box_keeps_its_handles_full_reach_inside_it()
    {
        using var folder = TempTheme.With(Small);
        using var session = folder.Open();
        var frame = Frame(CanvasGeometry.Of(session), "widgets[3]");

        Assert.Equal(SelectionHandle.TopLeft, frame.HitHandle(new SKPoint(705, 105), 1));
        Assert.Equal(SelectionHandle.Left, frame.HitHandle(new SKPoint(705, 150), 1));
        Assert.Null(frame.HitHandle(new SKPoint(750, 150), 1));
        Assert.Null(frame.HitHandle(new SKPoint(707, 150), 1));
    }

    static SKPoint Round(SKPoint point) => new(MathF.Round(point.X, 3), MathF.Round(point.Y, 3));

    static SKRect Round(SKRect rect) =>
        new(MathF.Round(rect.Left, 3), MathF.Round(rect.Top, 3), MathF.Round(rect.Right, 3), MathF.Round(rect.Bottom, 3));
}
