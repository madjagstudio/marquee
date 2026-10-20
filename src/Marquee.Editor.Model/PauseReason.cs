namespace Marquee.Editor.Model;

/// Why editing is paused (EditorSession.Pause), so the shell can offer what fits each.
public enum PauseReason
{
    /// Not paused: Document's text loads as a theme.
    None,

    /// Document's text, taken from outside the editor (a hand edit, a revert), is YAML but doesn't load as a theme.
    /// It is an undo step of its own, but undo and redo wait as in any pause (EditorSession.CanUndo), so the file is
    /// never written over without an explicit click; LoadError says why.
    Theme,

    /// layout.yaml, changed outside the editor, isn't YAML the editor can open, so Document is still the session's
    /// last text, not the file's. The file is the user's until it parses again or they revert; LoadError says why.
    File,
}
