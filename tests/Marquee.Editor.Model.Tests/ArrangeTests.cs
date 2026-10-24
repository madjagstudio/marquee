using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// Nudging with the arrow keys, and aligning and distributing two or more widgets, each one PlaceWidgets step.
public class ArrangeTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: shape, shape: rect, at: [100, 100], size: [100, 50] }\n" +
        "  - { type: shape, shape: rect, at: [400, 160], size: [50, 100] }\n" +
        "  - { type: shape, shape: rect, at: [250, 40], size: [41, 60] }\n" +
        "  - { type: shape, shape: rect, at: [700, 300], size: [40, 40], locked: true }\n";

    static readonly YamlPath[] Three = [YamlPath.Parse("widgets[0]"), YamlPath.Parse("widgets[1]"), YamlPath.Parse("widgets[2]")];

    /// Applies `command`, checks the text it leaves is `expected` and loads, and that it undoes in one step.
    static void AssertStep(EditorCommand command, string expected)
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        session.Apply(command);

        Assert.Equal(expected, session.Document.Text);
        Assert.Equal(1, session.History.Count);
        session.Undo();
        Assert.Equal(Layout, session.Document.Text);
    }

    static string Moved(params (string From, string To)[] changes) =>
        changes.Aggregate(Layout, (text, change) => text.Replace(change.From, change.To));

    [Theory]
    [InlineData(Alignment.Left, "at: [100, 100]", "at: [100, 160]", "at: [100, 40]")]
    [InlineData(Alignment.Center, "at: [225, 100]", "at: [250, 160]", "at: [254, 40]")] // centred on 275: 254.5, rounded to even
    [InlineData(Alignment.Right, "at: [350, 100]", "at: [400, 160]", "at: [409, 40]")]
    [InlineData(Alignment.Top, "at: [100, 40]", "at: [400, 40]", "at: [250, 40]")]
    [InlineData(Alignment.Middle, "at: [100, 125]", "at: [400, 100]", "at: [250, 120]")]
    [InlineData(Alignment.Bottom, "at: [100, 210]", "at: [400, 160]", "at: [250, 200]")]
    public void Each_alignment_lines_the_widgets_up_on_the_box_round_them(Alignment how, string first, string second, string third)
    {
        AssertStep(new AlignWidgets(Three, how),
            Moved(("at: [100, 100]", first), ("at: [400, 160]", second), ("at: [250, 40]", third)));
    }

    [Fact]
    public void Distributing_horizontally_evens_the_gaps_leaving_the_outermost_where_they_are()
    {
        // Left to right: 100–200, 250–291, 400–450. The gaps total 159 over two: 79.5 each, so the middle one goes to
        // 279.5, written to the nearest pixel.
        AssertStep(new DistributeWidgets(Three, Distribution.Horizontal), Moved(("at: [250, 40]", "at: [280, 40]")));
    }

    [Fact]
    public void Distributing_vertically_evens_the_gaps()
    {
        // Top to bottom: 40–100, 100–150, 160–260. The gaps total 10 over two: 5 each.
        AssertStep(new DistributeWidgets(Three, Distribution.Vertical), Moved(("at: [100, 100]", "at: [100, 105]")));
    }

    [Fact]
    public void Locked_widgets_are_left_out_and_fewer_than_two_change_nothing()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        session.Apply(new AlignWidgets([YamlPath.Parse("widgets[0]"), YamlPath.Parse("widgets[3]")], Alignment.Left));
        Assert.Equal(Layout, session.Document.Text);
        Assert.False(session.CanUndo);

        session.Apply(new AlignWidgets([.. Three, YamlPath.Parse("widgets[3]")], Alignment.Top));
        Assert.Equal(Moved(("at: [100, 100]", "at: [100, 40]"), ("at: [400, 160]", "at: [400, 40]")), session.Document.Text);
        Assert.Equal("Align top", session.History.NextUndo!.Label);
    }

    [Fact]
    public void Aligning_turned_widgets_uses_their_boxes_as_drawn_and_children_move_in_their_groups_coordinates()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - { type: shape, shape: rect, at: [100, 100], size: [100, 50] }\n" +
            "  - { type: shape, shape: rect, at: [600, 200], size: [200, 20], rotate: 90 }\n" +
            "  - type: group\n" +
            "    at: [1000, 100]\n" +
            "    size: [200, 200]\n" +
            "    children:\n" +
            "      - { type: shape, shape: rect, at: [50, 50], size: [20, 20] }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();

        session.Apply(new AlignWidgets(
            [YamlPath.Parse("widgets[0]"), YamlPath.Parse("widgets[1]"), YamlPath.Parse("widgets[2].children[0]")], Alignment.Left));

        // The turned rect moves by 590. Fitting moves the group's corner left so the aligned child remains visible.
        Assert.Equal(layout.Replace("at: [600, 200]", "at: [10, 200]").Replace("at: [50, 50]", "at: [0, 50]")
                .Replace("at: [1000, 100]", "at: [100, 100]").Replace("size: [200, 200]", "size: [1100, 200]"),
            session.Document.Text);
    }

    [Fact]
    public void Arrows_nudge_by_1_px_and_shift_arrows_by_10_with_a_run_making_one_step()
    {
        var time = new ManualTime();
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open(new EditorSessionOptions { Time = time });
        var chosen = new[] { YamlPath.Parse("widgets[0]"), YamlPath.Parse("widgets[3]") };

        session.Apply(NudgeWidgets.Arrow(chosen, ArrowKey.Right, shift: false));
        time.Advance(TimeSpan.FromMilliseconds(200));
        session.Apply(NudgeWidgets.Arrow(chosen, ArrowKey.Right, shift: false));
        time.Advance(TimeSpan.FromMilliseconds(200));
        session.Apply(NudgeWidgets.Arrow(chosen, ArrowKey.Down, shift: true));

        // The locked widget stays where it is.
        Assert.Equal(Moved(("at: [100, 100]", "at: [102, 110]")), session.Document.Text);
        Assert.Equal(1, session.History.Count);
        Assert.Equal("Nudge", session.History.NextUndo!.Label);
        session.Undo();
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void A_nudge_of_another_selection_is_a_step_of_its_own()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open(new EditorSessionOptions { Time = new ManualTime() });

        session.Apply(NudgeWidgets.Arrow([YamlPath.Parse("widgets[0]")], ArrowKey.Left, shift: false));
        session.Apply(NudgeWidgets.Arrow([YamlPath.Parse("widgets[1]")], ArrowKey.Up, shift: false));

        Assert.Equal(Moved(("at: [100, 100]", "at: [99, 100]"), ("at: [400, 160]", "at: [400, 159]")), session.Document.Text);
        Assert.Equal(2, session.History.Count);
    }

    [Fact]
    public void Nudges_are_in_the_coordinates_at_is_written_in()
    {
        Assert.Equal(new SKPoint(-10, 0), NudgeWidgets.Arrow([], ArrowKey.Left, shift: true).By);
        Assert.Equal(new SKPoint(0, 1), NudgeWidgets.Arrow([], ArrowKey.Down, shift: false).By);
    }
}
