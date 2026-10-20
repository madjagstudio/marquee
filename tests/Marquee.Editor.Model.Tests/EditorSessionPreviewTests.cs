using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// A drag on the canvas: while it lasts, frames draw the widgets where the drag has them, from the loaded theme
/// changed in memory, and the YAML isn't touched; letting go writes it once, as one undo step, and Esc puts it back.
public class EditorSessionPreviewTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "background:\n  - fill: \"#000000\"\n" +
        "widgets:\n" +
        "  - { type: shape, shape: rect, at: [10, 10], size: [40, 40], style: { fill: \"#FF0000\" } }\n" +
        "  - type: group\n" +
        "    at: [400, 100]\n" +
        "    size: [300, 200]\n" +
        "    children:\n" +
        "      - type: shape\n" +
        "        shape: rect\n" +
        "        at: [0, 0]\n" +
        "        size: [20, 20]\n" +
        "        style: { fill: \"#00FF00\" }\n";

    static readonly YamlPath Square = YamlPath.Parse("widgets[0]");
    static readonly YamlPath Child = YamlPath.Parse("widgets[1].children[0]");

    [Fact]
    public void A_drag_draws_the_widget_where_it_is_dragged_without_touching_the_yaml()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        session.BeginPreview();
        session.PreviewMove(Square, new SKPoint(60, 10));
        session.PreviewMove(Square, new SKPoint(110, 50));

        Assert.True(session.IsPreviewing);
        Assert.Equal(Layout, session.Document.Text);
        Assert.False(session.CanUndo);
        Frames.AssertNear(SKColors.Red, Frames.PixelAt(session, 130, 70));
        Frames.AssertNear(SKColors.Black, Frames.PixelAt(session, 30, 30));
    }

    [Fact]
    public void Letting_go_writes_the_yaml_once_as_one_step()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        session.BeginPreview();
        session.PreviewMove(Square, new SKPoint(60, 10));
        session.PreviewMove(Square, new SKPoint(110.5f, 50));

        session.CommitPreview();

        Assert.False(session.IsPreviewing);
        Assert.Equal(Layout.Replace("at: [10, 10]", "at: [110.5, 50]"), session.Document.Text);
        Assert.Equal(1, session.History.Count);
        Assert.Equal("Move", session.History.NextUndo!.Label);
        Frames.AssertNear(SKColors.Red, Frames.PixelAt(session, 130, 70));

        session.Undo();
        Assert.Equal(Layout, session.Document.Text);
        Frames.AssertNear(SKColors.Red, Frames.PixelAt(session, 30, 30));
    }

    [Fact]
    public void Esc_puts_everything_back()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        session.BeginPreview();
        session.PreviewMove(Square, new SKPoint(110, 50));

        session.CancelPreview();

        Assert.False(session.IsPreviewing);
        Assert.Equal(Layout, session.Document.Text);
        Assert.False(session.CanUndo);
        Frames.AssertNear(SKColors.Red, Frames.PixelAt(session, 30, 30));
        Frames.AssertNear(SKColors.Black, Frames.PixelAt(session, 130, 70));
    }

    [Fact]
    public void Resizing_and_turning_write_the_size_and_rotate()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        session.BeginPreview();

        session.PreviewResize(Square, SKRect.Create(10, 10, 80, 20));
        session.PreviewRotate(Square, 15);
        Assert.Equal(Layout, session.Document.Text);
        session.CommitPreview();

        Assert.Equal(Layout.Replace("size: [40, 40], style", "size: [80, 20], style").Replace("\"#FF0000\" } }", "\"#FF0000\" }, rotate: 15 }"),
            session.Document.Text);
        Assert.Equal("Rotate", session.History.NextUndo!.Label);
    }

    [Fact]
    public void A_child_moves_within_its_group_in_the_groups_coordinates()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        session.Select([Child]);
        session.BeginPreview();

        session.PreviewMove(Child, new SKPoint(50, 0));

        Frames.AssertNear(SKColors.Lime, Frames.PixelAt(session, 460, 110));
        Frames.AssertNear(SKColors.Black, Frames.PixelAt(session, 410, 110));

        session.CommitPreview();
        Assert.Equal(Layout.Replace("        at: [0, 0]", "        at: [50, 0]"), session.Document.Text);
        Assert.Equal([Child], session.Selection);
        Assert.Equal(SKRect.Create(450, 100, 20, 20), session.Widget(Child)!.Bounds);
    }

    [Fact]
    public void The_drags_placements_say_where_the_moved_widgets_are_until_it_ends()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        Assert.Empty(session.PreviewPlacements);
        session.BeginPreview();
        Assert.Empty(session.PreviewPlacements);

        session.PreviewMove(Child, new SKPoint(50, 0));
        session.PreviewRotate(Square, 30);

        Assert.Equal(
            [new WidgetPlacement(Child, SKRect.Create(50, 0, 20, 20), 0), new WidgetPlacement(Square, SKRect.Create(10, 10, 40, 40), 30)],
            session.PreviewPlacements);
        session.CancelPreview();
        Assert.Empty(session.PreviewPlacements);
    }

    [Fact]
    public void Several_widgets_drag_together_as_one_step()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        session.BeginPreview();

        session.PreviewMove(Square, new SKPoint(20, 10));
        session.PreviewMove(YamlPath.Parse("widgets[1]"), new SKPoint(410, 100));
        session.CommitPreview();

        Assert.Equal(Layout.Replace("at: [10, 10]", "at: [20, 10]").Replace("at: [400, 100]", "at: [410, 100]"), session.Document.Text);
        Assert.Equal(1, session.History.Count);
    }

    [Fact]
    public void The_showcases_clip_is_dragged_and_resized_in_its_group()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var radar = YamlPath.Parse("widgets[0].children[3]");
        var text = session.Document.Text;
        session.BeginPreview();

        session.PreviewResize(radar, SKRect.Create(30, 260, 200, 120));
        using (var frame = session.RenderFrame(TimeSpan.Zero))
            Assert.NotNull(frame);
        Assert.Equal(text, session.Document.Text);
        session.CommitPreview();

        Assert.Equal(SKRect.Create(54, 284, 200, 120), session.Widget(radar)!.Bounds);
        Assert.Equal("Resize", session.History.NextUndo!.Label);
        // The example is checked out with the PC's line endings, which the edit keeps.
        var eol = text.Contains("\r\n") ? "\r\n" : "\n";
        Assert.Equal(text.Replace($"at: [24, 250]{eol}        size: [252, 158]", $"at: [30, 260]{eol}        size: [200, 120]"),
            session.Document.Text);
    }

    [Fact]
    public void A_drag_that_ends_where_it_began_is_no_step()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        session.BeginPreview();
        session.PreviewMove(Square, new SKPoint(110, 50));
        session.PreviewMove(Square, new SKPoint(10, 10));

        session.CommitPreview();

        Assert.Equal(Layout, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Each_preview_change_asks_for_a_redraw()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var changes = new List<SessionChanges>();
        session.Changed += (_, what) => changes.Add(what);

        session.BeginPreview();
        session.PreviewMove(Square, new SKPoint(20, 10));
        session.CancelPreview();

        Assert.Equal([SessionChanges.Preview, SessionChanges.Preview, SessionChanges.Preview], changes);
    }

    [Fact]
    public void Nothing_else_changes_the_theme_during_a_drag()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        session.Apply(new SetValue(YamlPath.Parse("widgets[0].at[1]"), "12"));
        session.BeginPreview();

        Assert.Throws<InvalidOperationException>(() => session.BeginPreview());
        Assert.Throws<InvalidOperationException>(() => session.Apply(new SetValue(YamlPath.Parse("widgets[0].at[0]"), "20")));
        Assert.Throws<InvalidOperationException>(() => session.Undo());
        Assert.Throws<InvalidOperationException>(() => session.Redo());

        session.CancelPreview();
        Assert.Throws<InvalidOperationException>(() => session.PreviewMove(Square, new SKPoint(1, 1)));
        Assert.Throws<InvalidOperationException>(() => session.CommitPreview());
    }

    [Fact]
    public void A_widget_that_is_not_a_widget_or_uses_an_anchor_is_not_dragged()
    {
        using var folder = TempTheme.With(
            "canvas: { orientation: landscape }\nstyles:\n  red: &red { fill: \"#FF0000\" }\n" +
            "widgets:\n  - { type: shape, shape: rect, at: [0, 0], size: [10, 10], style: *red }\n");
        using var session = folder.Open();
        session.BeginPreview();

        Assert.False(session.Widget(Square)!.IsEditable);
        Assert.Throws<InvalidOperationException>(() => session.PreviewMove(Square, new SKPoint(5, 5)));
        Assert.Throws<ArgumentException>(() => session.PreviewMove(YamlPath.Parse("widgets[4]"), new SKPoint(5, 5)));
    }
}
