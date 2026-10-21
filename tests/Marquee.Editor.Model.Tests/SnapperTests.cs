using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// Snapping a dragged box to the canvas's edges and centre and to other widgets' edges and centres, within a few screen
/// pixels whatever the zoom, with the guide lines that show it.
public class SnapperTests
{
    internal const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: shape, shape: rect, at: [100, 100], size: [200, 100] }\n" +
        "  - { type: shape, shape: rect, at: [400, 300], size: [50, 50] }\n" +
        "  - { type: shape, shape: rect, at: [1000, 50], size: [10, 10], hidden: true }\n" +
        "  - { type: shape, shape: rect, at: [1500, 400], size: [40, 40] }\n";

    static readonly YamlPath Moving = YamlPath.Parse("widgets[1]");

    static Snapper Snapper(EditorSession session, params YamlPath[] moving) => new(CanvasGeometry.Of(session), moving);

    [Fact]
    public void An_edge_near_another_widgets_edge_snaps_to_it_with_a_guide_between_them()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var snap = Snapper(session, Moving).Snap(new SKRect(303, 300, 353, 350), zoom: 1);

        Assert.Equal(new SKPoint(-3, 0), snap.Offset);
        // From the top of the widget snapped to, down to the bottom of the dragged box.
        Assert.Equal([new SnapGuide(new SKPoint(300, 100), new SKPoint(300, 350))], snap.Guides);
    }

    [Fact]
    public void The_threshold_is_in_screen_pixels_so_it_reaches_further_zoomed_out()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var snapper = Snapper(session, Moving);
        var box = new SKRect(320, 300, 370, 350);

        Assert.Equal(SKPoint.Empty, snapper.Snap(box, zoom: 1).Offset);
        Assert.Empty(snapper.Snap(box, zoom: 1).Guides);
        Assert.Equal(new SKPoint(-20, 0), snapper.Snap(box, zoom: 0.25f).Offset);
        // Zoomed in, 3 theme pixels is 6 screen pixels, just within.
        Assert.Equal(SKPoint.Empty, snapper.Snap(new SKRect(304, 300, 354, 350), zoom: 2).Offset);
        Assert.Equal(new SKPoint(-3, 0), snapper.Snap(new SKRect(303, 300, 353, 350), zoom: 2).Offset);
    }

    [Fact]
    public void Centres_snap_to_the_canvas_centre_with_a_guide_across_the_canvas()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        // Its centre is 2 px right of the canvas's and 4 px below; its bottom is 2 px above the canvas's.
        var snap = Snapper(session, Moving).Snap(new SKRect(937, 434, 987, 478), zoom: 1);

        Assert.Equal(new SKPoint(-2, 2), snap.Offset);
        Assert.Contains(new SnapGuide(new SKPoint(960, 0), new SKPoint(960, 480)), snap.Guides);
        Assert.Contains(new SnapGuide(new SKPoint(0, 480), new SKPoint(1920, 480)), snap.Guides);
    }

    [Fact]
    public void Only_a_whole_pixel_move_snaps()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        // A 51 px box's centre, at 962.5, is 2.5 px from the canvas's: no whole move puts it there.
        var snap = Snapper(session, Moving).Snap(new SKRect(937, 220, 988, 271), zoom: 1);

        Assert.Equal(SKPoint.Empty, snap.Offset);
        Assert.Empty(snap.Guides);
    }

    [Fact]
    public void Hidden_widgets_and_the_dragged_ones_are_not_snapped_to()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        // Near the hidden widget's left edge, at 1000, and the dragged widget's own right edge, at 450.
        Assert.Equal(SKPoint.Empty, Snapper(session, Moving).Snap(new SKRect(1003, 300, 1053, 350), zoom: 1).Offset);
        Assert.Equal(SKPoint.Empty, Snapper(session, Moving).Snap(new SKRect(452, 300, 502, 350), zoom: 1).Offset);
    }

    [Fact]
    public void Only_the_edges_asked_for_snap()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        // Resizing by the right edge: the left one, 3 px from the other widget's right edge, stays put.
        var snap = Snapper(session, Moving).Snap(new SKRect(303, 300, 1497, 350), zoom: 1, SnapEdges.Right);

        Assert.Equal(new SKPoint(3, 0), snap.Offset);
        Assert.Equal([new SnapGuide(new SKPoint(1500, 300), new SKPoint(1500, 440))], snap.Guides);
    }
}
