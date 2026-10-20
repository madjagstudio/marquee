namespace Marquee.Editor.Model;

/// How a session keeps time and history. Tests give a manual clock.
public sealed record EditorSessionOptions
{
    /// The clock undo merging reads, and the empty value store's `now`.
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// How many undo steps are kept.
    public int UndoLimit { get; init; } = UndoStack.DefaultCapacity;

    /// How close together commands with one merge key must come to be one undo step.
    public TimeSpan MergeWindow { get; init; } = UndoStack.DefaultMergeWindow;
}
