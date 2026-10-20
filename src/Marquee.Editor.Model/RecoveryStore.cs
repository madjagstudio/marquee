using Marquee.Core.Files;
using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Crash recovery. Every change's text is written to EditorFolders.RecoveryFile, so a session that ends without closing
/// (a crash, Task Manager) leaves its latest text there, saved or not; a clean close deletes it. Opening the theme
/// again finds it, and offers it (Offer) when it differs from layout.yaml.
///
/// The writes are cheap and bounded: the first change arms a short timer, and changes until it fires ride along, so a
/// run of nudges writes a few times a second at most rather than once a key, and nothing is lost for longer than the
/// delay. A drag in a panel (EditorSession.IsInteracting) writes nothing until it ends, then its text once. Each write
/// is atomic (SafeFile), so a crash mid-write leaves the last whole text.
///
/// A write that fails, and a file left behind that can't be read, aren't silent: Problem says so, for the shell to show
/// ("crash recovery is off"), until a write succeeds.
///
/// Changes are taken on the UI thread (the session's Changed event); the writes happen on the clock's timer thread, and
/// ProblemChanged is posted back to the UI thread.
public sealed class RecoveryStore : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(200);

    readonly EditorSession _session;
    readonly SynchronizationContext _ui;
    readonly TimeSpan _delay;
    readonly ITimer _timer;

    /// Guards the fields below; never held while writing.
    readonly object _gate = new();

    /// Held while writing or deleting the file, so they happen in order.
    readonly object _writing = new();

    byte[]? _waiting;
    bool _closed;
    string? _problem;

    /// True during a drag in a panel: the timer writes nothing until it ends.
    bool _holding;

    /// The text changed during the drag under way, so its end writes it. UI thread only.
    bool _heldChange;

    /// Reads any text a session left behind (Offer) before this one writes anything. Construct it on the UI thread,
    /// right after the session opens, and offer the restore before the first change. `ui` is the UI thread's context,
    /// where ProblemChanged is raised.
    public RecoveryStore(EditorSession session, EditorFolders folders, SynchronizationContext ui, TimeProvider time,
        TimeSpan? delay = null)
    {
        _session = session;
        _ui = ui;
        _delay = delay ?? DefaultDelay;
        Path = folders.RecoveryFile(session.Folder);
        Offer = Find(Path, session.LayoutPath, out _problem);
        _timer = time.CreateTimer(_ => Write(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        session.Changed += OnChanged;
    }

    /// Where the text goes: EditorFolders.RecoveryFile.
    public string Path { get; }

    /// What a session that ended without closing left behind, when it differs from layout.yaml; null otherwise. It is
    /// read once, when this store is made.
    public RecoveryOffer? Offer { get; }

    /// Why crash recovery isn't keeping the session's text, in words for the user: the last write failed, or what a
    /// session left behind couldn't be read when this store was made. Null while all is well; a write that succeeds
    /// clears it.
    public string? Problem
    {
        get
        {
            lock (_gate) return _problem;
        }
    }

    /// Raised on the UI thread when Problem changes.
    public event EventHandler? ProblemChanged;

    /// For a clean close, after autosave has flushed: stops writing and deletes the file. Text that couldn't be saved
    /// should be left for recovery instead (Dispose).
    public void CloseCleanly()
    {
        Stop();
        lock (_writing)
        {
            try
            {
                File.Delete(Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left behind, it is offered only if it differs from layout.yaml, which it doesn't after a clean close.
            }
        }
    }

    /// Stops writing, first writing any text that waits, and leaves the file to be offered next time: for a close
    /// whose last save failed.
    public void Dispose()
    {
        Stop();
        // A drag in a panel cut short by the close: what it made is the latest text.
        if (_heldChange)
        {
            var bytes = _session.Document.ToBytes();
            lock (_gate) _waiting = bytes;
        }

        Write(evenIfClosed: true);
    }

    void Stop()
    {
        _session.Changed -= OnChanged;
        lock (_gate) _closed = true;
        _timer.Dispose();
    }

    /// On the UI thread: the text to write, the timer armed if it isn't already. During a drag in a panel the timer
    /// stops, and the drag's end (SessionChanges.Interaction) takes the text it made, or lets a text from before it go.
    void OnChanged(object? sender, SessionChanges changes)
    {
        if (_session.IsInteracting)
        {
            _heldChange |= changes.HasFlag(SessionChanges.Document);
            lock (_gate)
            {
                _holding = true;
                if (!_closed) _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            return;
        }

        var changed = changes.HasFlag(SessionChanges.Document) || (_heldChange && changes.HasFlag(SessionChanges.Interaction));
        _heldChange = false;
        var bytes = changed ? _session.Document.ToBytes() : null;
        lock (_gate)
        {
            var held = _holding;
            _holding = false;
            if (_closed || (bytes is null && !(held && _waiting is not null))) return;
            // A timer stopped by the drag isn't armed, whatever waits.
            var armed = _waiting is not null && !held;
            if (bytes is not null) _waiting = bytes;
            if (!armed) _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    void Write() => Write(evenIfClosed: false);

    void Write(bool evenIfClosed)
    {
        lock (_writing)
        {
            byte[]? bytes;
            lock (_gate)
            {
                // The timer of a drag in a panel writes nothing; its end arms it again.
                if ((_closed || _holding) && !evenIfClosed) return;
                bytes = _waiting;
                _waiting = null;
            }

            if (bytes is null) return;
            string? problem = null;
            try
            {
                SafeFile.WriteAllBytes(Path, bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Recovery is a safety net, not the save: a failed write waits for the next change, and the user is
                // told, without stopping them, that a crash now would lose what autosave hasn't saved.
                problem = $"Unsaved changes can't be kept for crash recovery: {ex.Message}";
            }

            SetProblem(problem);
        }
    }

    void SetProblem(string? problem)
    {
        lock (_gate)
        {
            if (problem == _problem) return;
            _problem = problem;
        }

        _ui.Post(_ => ProblemChanged?.Invoke(this, EventArgs.Empty), null);
    }

    /// The recovery file's text when it is there, readable, and not what layout.yaml holds. One that is there but
    /// can't be read, or isn't text the editor could take, isn't offered, and `problem` says so: the next change writes
    /// over it.
    static RecoveryOffer? Find(string path, string layout, out string? problem)
    {
        problem = null;
        byte[] bytes;
        string text;
        try
        {
            if (!File.Exists(path)) return null;
            bytes = File.ReadAllBytes(path);
            text = LayoutDocument.FromBytes(bytes).Text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LayoutEditException)
        {
            problem = $"Unsaved changes an earlier session left for crash recovery couldn't be read, so they aren't offered, and the next change replaces them: {ex.Message}";
            return null;
        }

        try
        {
            if (bytes.AsSpan().SequenceEqual(File.ReadAllBytes(layout))) return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // layout.yaml just opened, so this is a moment's lock: offer it, and the times tell the user which is newer.
        }

        return new RecoveryOffer(text, new DateTimeOffset(File.GetLastWriteTimeUtc(path)),
            new DateTimeOffset(File.GetLastWriteTimeUtc(layout)));
    }
}
