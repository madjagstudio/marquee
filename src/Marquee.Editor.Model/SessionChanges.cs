namespace Marquee.Editor.Model;

/// What a session's Changed event reports as changed; several at once for one step.
[Flags]
public enum SessionChanges
{
    None = 0,

    /// The layout text: a command, undo or redo.
    Document = 1,

    /// The loaded theme and its renderer, and with them the widget tree, or the load error.
    Theme = 2,

    Selection = 4,

    /// A drag's preview moved on, began or ended: the canvas redraws, and nothing else changed.
    Preview = 8,
}
