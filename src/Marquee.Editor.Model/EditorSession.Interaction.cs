namespace Marquee.Editor.Model;

/// A drag in a panel: a slider, the colour picker, a gradient's stops, dial, centre or radius. Unlike a drag on the
/// canvas (EditorSession.Preview.cs), each move is a command that changes the text, so the canvas and the panels show
/// it at once; but the drag is one interaction:
/// 1. BeginInteraction when it is grabbed;
/// 2. Apply for each move, with a merge key, as for any change;
/// 3. EndInteraction when it is let go.
///
/// While it lasts, its moves are one undo step however long the drag rests (UndoStack.BeginRun), and nothing is
/// written: autosave and recovery wait for its end (SessionChanges.Interaction), then save once, and a hand edit read
/// meanwhile waits too (ExternalChangeWatcher). Undo, redo, another drag and a canvas drag wait as well.
public sealed partial class EditorSession
{
    /// True between BeginInteraction and EndInteraction.
    public bool IsInteracting { get; private set; }

    /// Starts a drag in a panel. Throws InvalidOperationException during another one, or a canvas drag's preview.
    public void BeginInteraction()
    {
        ThrowIfDisposed();
        ThrowIfPreviewing();
        if (IsInteracting) throw new InvalidOperationException("A drag in a panel is already under way.");
        IsInteracting = true;
        History.BeginRun();
        Raise(SessionChanges.Interaction);
    }

    /// Ends the drag: its moves stay as one undo step, sealed, and are saved after it. Does nothing when there is no
    /// drag, so a control can call it whenever the mouse is let go or taken away.
    public void EndInteraction()
    {
        ThrowIfDisposed();
        if (!IsInteracting) return;
        IsInteracting = false;
        History.EndRun();
        Raise(SessionChanges.Interaction);
    }

    void ThrowIfInteracting()
    {
        if (IsInteracting) throw new InvalidOperationException("A drag in a panel is under way: let it go first.");
    }
}
