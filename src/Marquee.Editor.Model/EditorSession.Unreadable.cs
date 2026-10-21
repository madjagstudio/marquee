namespace Marquee.Editor.Model;

/// A layout.yaml changed outside the editor into text that isn't YAML it can open (a hand edit half done, a stray
/// bracket). ReplaceDocument refuses such text, so the session can't take it as its Document; instead it keeps its
/// last text and pauses on the file (PauseReason.File), with the error and its line, as it pauses on text that doesn't
/// load as a theme: commands are refused, and nothing is saved over the file (Autosaver), until text from outside is
/// taken (the file fixed, or "Revert to opened version"). Undo and redo are refused too (CanUndo), as in any pause: the
/// hand edit has no step of its own, so undoing would save over it. The shell's banner offers the revert, or keeping on
/// in the text editor.
public sealed partial class EditorSession
{
    /// LoadError before the file became unreadable, for when the file comes back as the session's own text: a theme
    /// error the session was paused on then is paused on again. Cleared whenever the session takes a text (Show).
    LoadError? _beforeUnreadable;

    /// Why editing is paused now; None when it isn't. Set with LoadError: by each text the session shows (Theme when it
    /// doesn't load, None when it does) and by PauseOnUnreadableFile (File).
    public PauseReason Pause { get; private set; }

    /// True while editing is paused because layout.yaml isn't YAML the editor can open: Document is the session's last
    /// text, not the file's.
    public bool IsFileUnreadable => Pause == PauseReason.File;

    /// Pauses editing because layout.yaml, changed outside the editor, isn't YAML it can open; `error` says why, with
    /// its line in the file. Throws InvalidOperationException during a drag's preview.
    public void PauseOnUnreadableFile(LoadError error)
    {
        ThrowIfDisposed();
        ThrowIfPreviewing();
        if (Pause != PauseReason.File) _beforeUnreadable = LoadError;
        LoadError = error;
        Pause = PauseReason.File;
        Raise(SessionChanges.Theme);
    }

    /// Ends the pause on an unreadable file when text from outside turns out to be the session's own text, so there is
    /// no step to make: the file was put back as it was, or a revert's text is what the session has. A theme error the
    /// session was paused on before stays.
    SessionChanges EndUnreadable()
    {
        LoadError = _beforeUnreadable;
        Pause = LoadError is null ? PauseReason.None : PauseReason.Theme;
        _beforeUnreadable = null;
        return SessionChanges.Theme;
    }
}
