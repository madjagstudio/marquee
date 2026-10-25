namespace Marquee.Editor.Model;

/// The session's undo and redo steps. A new step ends whatever could be redone. A step whose merge key equals the last
/// one's, pushed within the merge window of it, joins it, so the many changes of a slider or of nudges undo at once:
/// the window slides, counting from the latest change merged in. A merged step that ends where it began is dropped, and
/// the next step starts afresh rather than joining the one before it.
///
/// A run (BeginRun to EndRun) is a drag in a panel: its steps with the same key join however long apart they come, so
/// a slider held still for a while is still one step, and EndRun seals it, so the next drag starts its own.
///
/// Only the latest `capacity` steps are kept, the oldest going first.
///
/// Not thread-safe: the session uses it from the UI thread.
public sealed class UndoStack
{
    public const int DefaultCapacity = 500;

    /// Long enough for a slider's changes to arrive as one, short enough that two deliberate edits of a key don't.
    public static readonly TimeSpan DefaultMergeWindow = TimeSpan.FromSeconds(1);

    readonly TimeProvider _time;
    readonly int _capacity;
    readonly TimeSpan _mergeWindow;
    readonly LinkedList<UndoStep> _undo = new();
    readonly Stack<UndoStep> _redo = new();

    /// Set after an undo, a redo or Seal: the next step starts afresh rather than joining the one now on top.
    bool _sealed;

    public UndoStack(TimeProvider time, int capacity = DefaultCapacity, TimeSpan? mergeWindow = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _time = time;
        _capacity = capacity;
        _mergeWindow = mergeWindow ?? DefaultMergeWindow;
    }

    /// How many steps can be undone.
    public int Count => _undo.Count;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// The step Undo would take back, for its label; null when there is none.
    public UndoStep? NextUndo => _undo.Last?.Value;

    /// The step Redo would make again; null when there is none.
    public UndoStep? NextRedo => _redo.TryPeek(out var step) ? step : null;

    /// Adds a step, stamped with the time, or merges it into the last one (the class comment says when).
    public void Push(UndoStep step)
    {
        var now = _time.GetUtcNow();
        step = step with { At = now };
        _redo.Clear();

        if (!_sealed && _undo.Last?.Value is { } last && step.MergeKey is not null && Equals(step.MergeKey, last.MergeKey)
            && (InRun || now - last.At <= _mergeWindow))
        {
            _undo.RemoveLast();
            step = last with { After = step.After, SelectionAfter = step.SelectionAfter, At = now };
            if (step.Before == step.After)
            {
                // The step now on top was made before this one began, so nothing joins it.
                _sealed = true;
                return;
            }
        }

        _sealed = false;
        _undo.AddLast(step);
        while (_undo.Count > _capacity) _undo.RemoveFirst();
    }

    /// Takes the last step off for undoing, ready to be redone; null when there is none. The caller restores its Before
    /// text and SelectionBefore.
    public UndoStep? Undo()
    {
        if (_undo.Last?.Value is not { } step) return null;
        _undo.RemoveLast();
        _redo.Push(step);
        _sealed = true;
        return step;
    }

    /// Takes the last undone step back for redoing; null when there is none. The caller restores its After text and
    /// SelectionAfter.
    public UndoStep? Redo()
    {
        if (!_redo.TryPop(out var step)) return null;
        _undo.AddLast(step);
        _sealed = true;
        return step;
    }

    /// Keeps the next step from merging into the last one, whatever its key: a slider let go, then grabbed again.
    public void Seal() => _sealed = true;

    /// True between BeginRun and EndRun.
    public bool InRun { get; private set; }

    /// Starts a run: the drag's first step starts afresh, and the steps after it with its key join it, without the
    /// merge window, until EndRun.
    public void BeginRun()
    {
        InRun = true;
        _sealed = true;
    }

    /// Ends a run and seals its step. Does nothing when there is no run.
    public void EndRun()
    {
        if (!InRun) return;
        InRun = false;
        _sealed = true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _sealed = false;
    }
}
