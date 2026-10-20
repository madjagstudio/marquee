using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// The steps undo and redo walk through: whole texts before and after, with the selection, merged while a slider or a
/// run of nudges keeps changing the same thing, and the last 500 kept.
public class UndoStackTests
{
    static readonly IReadOnlyList<YamlPath> None = [];
    static readonly IReadOnlyList<YamlPath> First = [YamlPath.Parse("widgets[0]")];

    static UndoStep Step(string before, string after, object? mergeKey = null, IReadOnlyList<YamlPath>? selection = null) =>
        new("Set size", before, after, None, selection ?? None) { MergeKey = mergeKey };

    [Fact]
    public void Undo_and_redo_walk_the_steps_back_and_forth()
    {
        var stack = new UndoStack(new ManualTime());
        stack.Push(Step("a", "b"));
        stack.Push(Step("b", "c", selection: First));

        Assert.Equal("c", stack.Undo()!.After);
        Assert.Equal("a", stack.Undo()!.Before);
        Assert.Null(stack.Undo());
        Assert.Equal("b", stack.Redo()!.After);
        Assert.Equal(First, stack.Redo()!.SelectionAfter);
        Assert.Null(stack.Redo());
    }

    [Fact]
    public void A_new_step_ends_what_could_be_redone()
    {
        var stack = new UndoStack(new ManualTime());
        stack.Push(Step("a", "b"));
        stack.Undo();

        stack.Push(Step("a", "x"));

        Assert.False(stack.CanRedo);
        Assert.Equal("x", stack.NextUndo!.After);
    }

    [Fact]
    public void Steps_with_the_same_merge_key_inside_the_window_become_one()
    {
        var time = new ManualTime();
        var stack = new UndoStack(time);

        stack.Push(Step("a", "b", "size"));
        time.Advance(TimeSpan.FromMilliseconds(900));
        stack.Push(Step("b", "c", "size", First));
        time.Advance(TimeSpan.FromMilliseconds(900));
        stack.Push(Step("c", "d", "size"));

        Assert.Equal(1, stack.Count);
        var step = stack.NextUndo!;
        Assert.Equal(("a", "d"), (step.Before, step.After));
        Assert.Equal(None, step.SelectionAfter);
    }

    [Fact]
    public void A_step_after_the_window_or_with_another_key_or_none_is_a_step_of_its_own()
    {
        var time = new ManualTime();
        var stack = new UndoStack(time);

        stack.Push(Step("a", "b", "size"));
        time.Advance(UndoStack.DefaultMergeWindow + TimeSpan.FromMilliseconds(1));
        stack.Push(Step("b", "c", "size"));
        stack.Push(Step("c", "d", "color"));
        stack.Push(Step("d", "e"));
        stack.Push(Step("e", "f"));

        Assert.Equal(5, stack.Count);
    }

    [Fact]
    public void Merge_keys_compare_by_value()
    {
        var stack = new UndoStack(new ManualTime());

        stack.Push(Step("a", "b", YamlPath.Parse("widgets[0].at")));
        stack.Push(Step("b", "c", YamlPath.Parse("widgets[0].at")));

        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void Nothing_merges_into_a_step_after_an_undo_or_a_redo_or_a_seal()
    {
        var stack = new UndoStack(new ManualTime());
        stack.Push(Step("a", "b", "size"));
        stack.Push(Step("b", "c", "color"));
        stack.Undo();

        stack.Push(Step("b", "x", "size"));
        Assert.Equal(2, stack.Count);

        stack.Seal();
        stack.Push(Step("x", "y", "size"));
        Assert.Equal(3, stack.Count);
    }

    [Fact]
    public void A_merged_step_back_where_it_started_is_dropped()
    {
        var stack = new UndoStack(new ManualTime());
        stack.Push(Step("a", "b"));
        stack.Push(Step("b", "c", "size"));

        stack.Push(Step("c", "b", "size"));

        Assert.Equal(1, stack.Count);
        Assert.Equal("b", stack.NextUndo!.After);
    }

    [Fact]
    public void Only_the_last_500_steps_are_kept()
    {
        var stack = new UndoStack(new ManualTime());

        for (var i = 0; i < 520; i++)
            stack.Push(Step(i.ToString(), (i + 1).ToString()));

        Assert.Equal(500, stack.Count);
        UndoStep? last = null;
        while (stack.Undo() is { } step) last = step;
        Assert.Equal("20", last!.Before);
    }

    [Fact]
    public void A_smaller_capacity_and_window_can_be_given()
    {
        var time = new ManualTime();
        var stack = new UndoStack(time, capacity: 2, mergeWindow: TimeSpan.FromSeconds(5));

        stack.Push(Step("a", "b", "k"));
        time.Advance(TimeSpan.FromSeconds(4));
        stack.Push(Step("b", "c", "k"));
        stack.Push(Step("c", "d"));
        stack.Push(Step("d", "e"));

        Assert.Equal(2, stack.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new UndoStack(time, capacity: 0));
    }
}
