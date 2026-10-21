using Marquee.Core.Themes;

namespace Marquee.Editor.Model;

/// Saves the session's text about a second after the last change, so the tray's hot reload shows each edit without a
/// Save button. The write is atomic (LayoutFile, through AtomicFile), so the tray never reads half a file. The opened
/// version is copied first, before the session's first save.
///
/// It never writes over a hand edit (LayoutFile.Write): only while the file holds what the editor last wrote or read,
/// and only text made since the session last took text from outside. A file changed outside is left for the external
/// change watcher, which reloads it as one undo step or pauses on it, and is looked at again a second later until it
/// has. A change made while a write was in flight is saved after it.
///
/// A save that fails (the file locked, the disk full, a link on the path) leaves the old file as it was; the state says
/// why, the text waits, and it is tried again later each time (1 s, 2 s, 4 s and so on, up to 30 s), or sooner when
/// another change comes. A theme folder or layout.yaml that is gone (moved, renamed or deleted while open) is never made
/// again: the state says so (SaveProblem.ThemeGone), and SaveTo saves the text to a folder the user chooses. Nothing is
/// saved while the session is paused: its text is a hand edit that doesn't load, or the file doesn't parse at all, and
/// either way the file is the user's to fix. Nor during a drag in a panel (EditorSession.IsInteracting): the tray would
/// reload the theme over and over, and a rest in the drag would save half of it; its text is saved once, after it ends.
///
/// Changes are taken on the UI thread (the session's Changed event), where the text to save is turned into bytes; the
/// write happens on the clock's timer thread, one at a time, and StateChanged is posted back to the UI thread.
/// Disposing stops it without saving what waits: a clean close calls Flush first.
public sealed class Autosaver : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    /// How long Flush waits for the save on a close. A save is a small local write and takes milliseconds; one stuck in
    /// the file system (a network share, a OneDrive placeholder downloading while offline) mustn't hold the window open.
    public static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    readonly EditorSession _session;
    readonly LayoutFile _file;
    readonly OpenedBackup? _backup;
    readonly SynchronizationContext _ui;
    readonly TimeProvider _time;
    readonly TimeSpan _delay;
    readonly ITimer _timer;

    /// Guards the fields below; never held while writing.
    readonly object _gate = new();

    /// Held while a save takes the waiting bytes and writes them, so saves go out in order.
    readonly object _writing = new();

    /// The bytes to write, and LayoutFile.Taken when they were made.
    byte[]? _waiting;
    long _waitingTaken;

    /// The session's latest bytes, waiting or not (null while paused), and LayoutFile.Taken when they were made: what
    /// the file should end up holding, checked after each write in case the session moved on while it was in flight.
    byte[]? _latest;
    long _latestTaken;

    /// True during a drag in a panel (EditorSession.IsInteracting): the timer saves nothing until it ends.
    bool _holding;

    int _failures;
    SaveState _state = SaveState.Saved;
    bool _disposed;

    /// `ui` is the UI thread's context, where StateChanged is raised. Construct it on the UI thread.
    public Autosaver(EditorSession session, LayoutFile file, OpenedBackup? backup, SynchronizationContext ui, TimeProvider time,
        TimeSpan? delay = null)
    {
        _session = session;
        _file = file;
        _backup = backup;
        _ui = ui;
        _time = time;
        _delay = delay ?? DefaultDelay;
        _timer = time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        session.Changed += OnChanged;
    }

    /// Where saving stands now.
    public SaveState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    /// Raised on the UI thread when State changes, with the new state.
    public event EventHandler<SaveState>? StateChanged;

    /// Saves what waits now: for a clean close. True when the file holds the session's text (nothing waited, or it was
    /// written); false when the write failed, and State says why, or when the file holds a change from outside the
    /// session hasn't taken, so recovery should keep the session's text. While the session is paused nothing waits, so
    /// it is true: the file is the user's.
    ///
    /// Bounded: it waits at most `timeout` (FlushTimeout when null) for the save, a save already under way included,
    /// and is false when that runs out. The save runs on a thread of its own, started at once, so a busy thread pool
    /// can't keep it from starting and turn a clean close into one that falls back to recovery. Past the bound it goes
    /// on by itself and lands if it can; recovery keeps the text meanwhile (RecoveryStore.Dispose), and offers it next
    /// time only if the file doesn't hold it.
    ///
    /// On the UI thread. A drag in a panel still under way (a close with the mouse held) is ended first, so what it
    /// made is saved rather than left behind.
    public bool Flush(TimeSpan? timeout = null)
    {
        if (_session.IsInteracting) _session.EndInteraction();
        lock (_gate)
        {
            if (_disposed) return false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        var saved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                saved.SetResult(Save());
            }
            catch (Exception ex)
            {
                saved.SetException(ex);
            }
        })
        {
            // A save stuck past the bound doesn't keep the process alive: the file is replaced whole or not at all.
            IsBackground = true,
            Name = "Marquee editor flush",
        };
        thread.Start();
        return thread.Join(timeout ?? FlushWait) && saved.Task.GetAwaiter().GetResult();
    }

    /// For "Save to a folder…" when the theme's folder or its layout.yaml is gone (SaveProblem.ThemeGone): writes the
    /// session's text as layout.yaml in `folder`, which the user chose. A layout.yaml already there (another theme's,
    /// most likely) is replaced only when `replace`: otherwise nothing is written and it returns null, for the shell to
    /// ask the user and call again. The folder must exist, and isn't made (DirectoryNotFoundException); a link on the
    /// path is refused, as for a save (LayoutFile.WriteCopy). Returns the file written. Nothing waits after it, so the shell closes the session cleanly (Flush, then
    /// RecoveryStore.CloseCleanly) and opens the theme from that folder. On the UI thread; throws the IOException or
    /// UnauthorizedAccessException of a write that failed, and nothing changes.
    public string? SaveTo(string folder, bool replace = false)
    {
        var bytes = _session.Document.ToBytes();
        var path = Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), ThemeLoader.LayoutFileName);
        if (!_file.WriteCopy(path, bytes, replace)) return null;
        lock (_gate)
        {
            if (_disposed) return path;
            _waiting = null;
            _latest = null;
            _failures = 0;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            SetState(SaveState.Saved);
        }

        return path;
    }

    public void Dispose()
    {
        _session.Changed -= OnChanged;
        lock (_gate)
        {
            _disposed = true;
            _waiting = null;
            _latest = null;
        }

        _timer.Dispose();
    }

    /// How long Flush waits when it is given no timeout: FlushTimeout. Tests make it wait as long as the save takes, so
    /// none of them hangs on how busy the machine is.
    internal TimeSpan FlushWait { get; set; } = FlushTimeout;

    /// For tests: runs on the saving thread after the bytes to write are taken and before they are written, so a test
    /// can do what another thread might do meanwhile.
    internal Action? BeforeWrite { get; set; }

    /// On the UI thread: the session's text, to save unless the file holds it already or editing is paused. During a
    /// drag in a panel nothing is written: the drag's end (SessionChanges.Interaction) brings the text it made.
    void OnChanged(object? sender, SessionChanges changes)
    {
        if ((changes & (SessionChanges.Document | SessionChanges.Theme | SessionChanges.Interaction)) == 0) return;
        if (_session.IsInteracting)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _holding = true;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                if (changes.HasFlag(SessionChanges.Document) && _state.Status == SaveStatus.Saved) SetState(SaveState.Saving);
            }

            return;
        }

        var bytes = _session.IsPaused ? null : _session.Document.ToBytes();
        // Read on the UI thread, where the session takes text from outside, so it is the count these bytes are made at.
        var taken = _file.Taken;
        lock (_gate)
        {
            if (_disposed) return;
            _holding = false;
            (_latest, _latestTaken) = (bytes, taken);
            if (bytes is null || _file.IsLast(bytes))
            {
                _waiting = null;
                _failures = 0;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                SetState(SaveState.Saved);
                return;
            }

            (_waiting, _waitingTaken) = (bytes, taken);
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
            // After a failure the state stays Failed, with its reason, until a save gets through.
            if (_state.Status == SaveStatus.Saved) SetState(SaveState.Saving);
        }
    }

    /// The timer's save, unless a drag in a panel is under way: a retry or a save armed before it began waits for its
    /// end, which arms the timer again.
    void OnTimer()
    {
        lock (_gate)
        {
            if (_holding) return;
        }

        Save();
    }

    /// Writes the waiting bytes, if any. True when nothing is left waiting.
    bool Save()
    {
        lock (_writing)
        {
            byte[]? bytes;
            long taken;
            lock (_gate)
            {
                if (_disposed) return false;
                (bytes, taken) = (_waiting, _waitingTaken);
            }

            if (bytes is null) return true;
            string? reason = null;
            var problem = SaveProblem.Write;
            var result = LayoutWrite.Written;
            try
            {
                try
                {
                    _backup?.EnsureWritten();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    reason = $"Couldn't keep a copy of the opened version, so {ThemeLoader.LayoutFileName} wasn't saved: {ex.Message}";
                    problem = SaveProblem.Backup;
                }

                if (reason is null)
                {
                    BeforeWrite?.Invoke();
                    result = _file.Write(bytes, taken);
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
            {
                // Moved, renamed or deleted while open. LayoutFile makes neither again: a half theme under the old name
                // would be all the tray found there, and every later save would go to it.
                (reason, problem) = (Gone(), SaveProblem.ThemeGone);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                reason = $"Couldn't save {ThemeLoader.LayoutFileName}: {ex.Message}";
            }

            lock (_gate)
            {
                if (_disposed) return false;
                // A change that came while writing waits on its own timer, which the change set.
                var newer = !ReferenceEquals(_waiting, bytes);
                if (reason is not null)
                {
                    _failures++;
                    var retry = RetryDelay(_failures);
                    if (!newer) _timer.Change(retry, Timeout.InfiniteTimeSpan);
                    SetState(SaveState.Failed(reason, _time.GetUtcNow() + (newer ? _delay : retry), problem));
                    return false;
                }

                if (result == LayoutWrite.ChangedOutside)
                {
                    // The watcher has been told (LayoutFile.ChangedOutside). Until the session takes the change, the
                    // text waits and the file is looked at again, so a read the watcher gave up on isn't the end.
                    if (!newer) _timer.Change(_delay, Timeout.InfiniteTimeSpan);
                    return false;
                }

                // Written, or Stale: made before the session took text from outside, which its next change saves.
                _failures = 0;
                if (!newer) _waiting = null;
                if (_waiting is null) WaitForLatest();
                SetState(_waiting is null ? SaveState.Saved : SaveState.Saving);
                return _waiting is null;
            }
        }
    }

    /// Why the theme is gone, in words for the user.
    string Gone() =>
        Directory.Exists(Path.GetDirectoryName(_file.Path))
            ? $"{ThemeLoader.LayoutFileName} is gone from the theme's folder (moved, renamed or deleted), so it wasn't saved, or made again. Your changes are kept for recovery; you can save them to a folder you choose."
            : $"The theme's folder is gone (moved, renamed or deleted), so {ThemeLoader.LayoutFileName} wasn't saved, and the folder wasn't made again. Your changes are kept for recovery; you can save them to a folder you choose.";

    /// With _gate held and nothing waiting: the session's latest bytes wait again when the file doesn't hold them, as
    /// when the session moved on while a write was in flight (an undo back to the text the file held before it). Not
    /// bytes made before the session last took text from outside: its taking them raises a change of its own.
    void WaitForLatest()
    {
        if (_latest is null || _latestTaken != _file.Taken || _file.IsLast(_latest)) return;
        (_waiting, _waitingTaken) = (_latest, _latestTaken);
        _timer.Change(_delay, Timeout.InfiniteTimeSpan);
    }

    /// 1 s after the first failure, twice as long after each one after it, up to MaxRetryDelay.
    TimeSpan RetryDelay(int failures) =>
        TimeSpan.FromTicks(Math.Min(MaxRetryDelay.Ticks, DefaultDelay.Ticks << Math.Min(failures - 1, 16)));

    /// With _gate held.
    void SetState(SaveState state)
    {
        if (state == _state) return;
        _state = state;
        _ui.Post(_ => StateChanged?.Invoke(this, state), null);
    }
}
