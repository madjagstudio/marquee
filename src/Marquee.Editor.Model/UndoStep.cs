using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// One undoable change: the whole layout text before and after it, and the selection either side, so undo and redo
/// swap texts and can't drift from what was there. Label names it for the menu ("Undo Move").
public sealed record UndoStep(string Label, string Before, string After, IReadOnlyList<YamlPath> SelectionBefore,
    IReadOnlyList<YamlPath> SelectionAfter)
{
    /// Steps with equal keys (by Equals) pushed within the stack's merge window become one; null never merges.
    public object? MergeKey { get; init; }

    /// When the step was pushed, or last merged into: the merge window counts from here.
    public DateTimeOffset At { get; init; }
}
