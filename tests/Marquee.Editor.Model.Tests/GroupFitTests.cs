using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

public class GroupFitTests
{
    const string Layout = """
        canvas: { orientation: landscape }
        background: [{ fill: "#000000" }]
        widgets:
          - type: group
            at: [100, 100]
            size: [100, 100]
            children:
              - { type: shape, shape: rect, at: [10, 10], size: [30, 30], style: { fill: "#00FF00" } }
              - { type: shape, shape: rect, at: [60, 60], size: [20, 20] }
        """;
    static readonly YamlPath Group = YamlPath.Parse("widgets[0]");
    static readonly YamlPath Child = Group.Child("children").Item(0);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Moving_below_the_group_fits_draws_and_undoes_together(bool drag)
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        if (drag)
        {
            var moving = CanvasDrag.Move(session, [Child], new SKPoint(120, 120))!;
            moving.Update(new SKPoint(120, 240), DragKeys.Alt, 1);
            // The child must stay visible while the pointer is still down too.
            AssertGreen(session, 120, 240);
            moving.Commit();
        }
        else session.Apply(new NudgeWidgets([Child], new SKPoint(0, 120)));

        Assert.Equal(160, session.Widget(Group)!.LocalBounds.Height);
        AssertGreen(session, 120, 240);
        Assert.Equal(1, session.History.Count);
        session.Undo();
        Assert.Equal(Layout, session.Document.Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(90)]
    public void Fitting_left_and_above_keeps_every_child_at_its_intended_screen_position(int turn)
    {
        using var folder = TempTheme.With(Layout.Replace("size: [100, 100]", $"size: [100, 100]\n    rotate: {turn}"));
        using var session = folder.Open();
        var before = CanvasGeometry.Of(session);
        var sibling = Group.Child("children").Item(1);
        var siblingCorners = before.CornersOf(sibling);
        var delta = before.ParentFrame(Child).MapVector(-40, -30);
        var childCorners = before.CornersOf(Child).Select(p => p + delta).ToArray();
        session.Apply(new NudgeWidgets([Child], new SKPoint(-40, -30)));
        Assert.Equal(new SKPoint(0, 0), session.Widget(Child)!.LocalBounds.Location);
        Assert.Equal(new SKSize(130, 120), session.Widget(Group)!.LocalBounds.Size);
        var after = CanvasGeometry.Of(session);
        AssertCorners(childCorners, after.CornersOf(Child));
        AssertCorners(siblingCorners, after.CornersOf(sibling));
        if (turn == 0) Assert.Equal(new SKPoint(70, 80), session.Widget(Group)!.LocalBounds.Location);
    }

    [Theory]
    [InlineData("paste")]
    [InlineData("duplicate")]
    [InlineData("add")]
    [InlineData("resize")]
    [InlineData("property-size")]
    [InlineData("property-at")]
    public void Commands_fit_the_destination_in_one_undo_step(string command)
    {
        using var folder = TempTheme.With(Layout.Replace("size: [100, 100]", "size: [80, 80]"));
        using var session = folder.Open();
        var original = session.Document.Text;
        session.Apply(command switch
        {
            "paste" => new PasteWidgets("{ type: shape, shape: rect, at: [80, 80], size: [40, 40] }", Group),
            "duplicate" => new DuplicateWidgets([Group.Child("children").Item(1)]),
            "add" => new AddWidget("text", Group),
            "resize" => new PlaceWidgets([new(Child, SKRect.Create(10, 10, 120, 130), 0)]),
            "property-size" => new SetValue(Child.Child("size"), new YamlList(["120", "130"])),
            _ => new SetValue(Child.Child("at").Item(0), "120"),
        });
        var box = session.Widget(Group)!.LocalBounds;
        foreach (var child in session.Widget(Group)!.Children)
        {
            Assert.True(child.LocalBounds.Left >= 0 && child.LocalBounds.Top >= 0);
            Assert.True(child.LocalBounds.Right <= box.Width && child.LocalBounds.Bottom <= box.Height);
        }
        Assert.Equal(1, session.History.Count);
        session.Undo();
        Assert.Equal(original, session.Document.Text);
    }

    [Fact]
    public void A_nested_child_grows_every_ancestor()
    {
        var nested = "canvas: { orientation: landscape }\nwidgets: [{ type: group, at: [100, 100], size: [80, 80], children: [{ type: group, at: [10, 10], size: [50, 50], children: [{ type: shape, shape: rect, at: [10, 10], size: [20, 20] }] }] }]";
        using var folder = TempTheme.With(nested);
        using var session = folder.Open();
        session.Apply(new NudgeWidgets([Child.Child("children").Item(0)], new SKPoint(100, 100)));
        Assert.Equal(new SKSize(130, 130), session.Widget(Child)!.LocalBounds.Size);
        Assert.Equal(new SKSize(140, 140), session.Widget(Group)!.LocalBounds.Size);
    }

    [Fact]
    public void Moving_a_layer_into_a_group_fits_it_without_moving_either_child_on_screen()
    {
        var layout = Layout + "\n  - { type: shape, shape: rect, at: [10, 10], size: [20, 20] }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();
        var before = CanvasGeometry.Of(session).CornersOf(Child);
        session.Apply(new MoveLayer(YamlPath.Parse("widgets[1]"), Group, 2));
        Assert.Equal(new SKPoint(10, 10), session.Widget(Group)!.LocalBounds.Location);
        AssertCorners(before, CanvasGeometry.Of(session).CornersOf(Child));
        Assert.Equal(new SKPoint(10, 10), CanvasGeometry.Of(session).BoundsOf(Group.Child("children").Item(2)).Location);
        session.Undo();
        Assert.Equal(layout, session.Document.Text);
    }

    [Fact]
    public void Fit_does_not_shrink_or_rewrite_a_group_that_already_contains_its_children()
    {
        var document = LayoutDocument.Open(Layout);
        Assert.Same(document, GroupFit.Fit(document, Group));
    }

    [Fact]
    public void Fit_leaves_a_group_without_size_alone()
    {
        var document = LayoutDocument.Open(Layout.Replace("    size: [100, 100]\n", ""));
        Assert.Same(document, GroupFit.Fit(document, Group));
    }

    [Fact]
    public void Fit_accounts_for_a_childs_rotated_box_and_is_idempotent()
    {
        var document = LayoutDocument.Open(Layout.Replace("at: [10, 10], size: [30, 30]", "at: [70, 70], size: [30, 30], rotate: 45"));
        var fitted = GroupFit.Fit(document, Group);
        Assert.Contains("size: [107, 107]", fitted.Text);
        Assert.Equal(fitted.Text, GroupFit.Fit(fitted, Group).Text);
    }

    [Fact]
    public void Several_children_are_placed_before_the_group_is_fitted()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var sibling = Group.Child("children").Item(1);
        session.Apply(new PlaceWidgets([new(Child, SKRect.Create(-30, -20, 30, 30), 0),
            new(sibling, SKRect.Create(130, 120, 20, 20), 0)]));
        Assert.Equal(new SKPoint(70, 80), session.Widget(Group)!.LocalBounds.Location);
        Assert.Equal(new SKSize(180, 160), session.Widget(Group)!.LocalBounds.Size);
        Assert.Equal(new SKPoint(230, 220), CanvasGeometry.Of(session).BoundsOf(sibling).Location);
    }

    static void AssertGreen(EditorSession session, int x, int y)
    {
        using var image = session.RenderFrame(TimeSpan.Zero);
        using var pixels = SKBitmap.FromImage(image!);
        Assert.Equal(SKColors.Lime, pixels.GetPixel(x, y));
    }

    static void AssertCorners(SKPoint[] expected, SKPoint[] actual)
    {
        for (var i = 0; i < expected.Length; i++)
            Assert.True(SKPoint.Distance(expected[i], actual[i]) < 0.001f, $"{expected[i]} != {actual[i]}");
    }
}
