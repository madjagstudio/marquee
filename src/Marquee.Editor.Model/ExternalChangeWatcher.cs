using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Hand edits while the editor is open. It watches the theme folder, and when layout.yaml holds bytes that aren't the
/// last the editor wrote or read (LayoutFile), it reloads them into the session as one undo step, "Changed outside the
/// editor". Text that doesn't load as a theme pauses editing with its error and line (the session does that); text
/// that isn't YAML the editor can open pauses it too (PauseOnUnreadableFile), so autosave never writes over a file
/// the user is still fixing. Either way the bytes are taken (LayoutFile.Accept) before the session sees them, so
/// autosave can't write text made before over them; and autosave hands over a change it finds when it goes to write
/// (LayoutFile.ChangedOutside), so one that lands just before a save is read at once.
///
/// Like ThemeWatcher, it waits for a burst of file events to settle, and a file it can't read yet (another program
/// mid-save) is read again shortly, a few times. File events arrive on a thread-pool thread and the file is read on
/// the clock's timer thread; the session is single-threaded, so what was read is posted to the UI thread's context.
/// A change that arrives during a drag, on the canvas or in a panel, waits for the drag to end: a canvas drag takes no
/// change until it ends, and a panel drag is one undo step, which a change in its middle would split.
public sealed class ExternalChangeWatcher : IDisposable
{
    public const string Label = "Changed outside the editor";

    public static readonly TimeSpan DefaultDebounce = ThemeWatcher.DefaultDebounce;

    const int MaxUnreadableRetries = 5;

    readonly EditorSession _session;
    readonly LayoutFile _file;
    readonly SynchronizationContext _ui;
    readonly TimeSpan _debounce;
    readonly ITimer _timer;
    readonly object _gate = new();
    FileSystemWatcher? _watcher;
    int _retries;
    bool _disposed;

    /// Bytes read during a drag (a canvas preview or a panel interaction), taken when it ends. UI thread only.
    byte[]? _deferred;

    /// `ui` is the UI thread's context. Construct it on the UI thread; Start begins watching.
    public ExternalChangeWatcher(EditorSession session, LayoutFile file, SynchronizationContext ui, TimeProvider time,
        TimeSpan? debounce = null)
    {
        _session = session;
        _file = file;
        _ui = ui;
        _debounce = debounce ?? DefaultDebounce;
        _timer = time.CreateTimer(_ => Read(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        session.Changed += OnSessionChanged;
        file.ChangedOutside += OnChangedOutside;
    }

    /// Watches the theme folder, and looks once now, for a change made while the theme was opening.
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is not null) return;
            // The folder, not the file: a save that renames a new file over layout.yaml (many editors', and
            // File.Replace's) is a rename in the folder.
            _watcher = new FileSystemWatcher(_session.Folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Error += (_, _) => Check();
            _watcher.EnableRaisingEvents = true;
        }

        Check();
    }

    public void Dispose()
    {
        _session.Changed -= OnSessionChanged;
        _file.ChangedOutside -= OnChangedOutside;
        FileSystemWatcher? watcher;
        lock (_gate)
        {
            _disposed = true;
            watcher = _watcher;
        }

        watcher?.Dispose();
        _timer.Dispose();
    }

    /// Looks at layout.yaml after the debounce, as a file event does. Any thread.
    internal void Check()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _retries = 0;
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (IsLayout(e.Name) || (e is RenamedEventArgs renamed && IsLayout(renamed.OldName))) Check();
    }

    /// Autosave found the file changed when it went to write it (LayoutFile.Write): look now, not at the next event.
    void OnChangedOutside(object? sender, EventArgs e) => Check();

    static bool IsLayout(string? name) => string.Equals(name, ThemeLoader.LayoutFileName, StringComparison.OrdinalIgnoreCase);

    /// On the timer thread: the file's bytes, posted to the UI thread when they are new.
    void Read()
    {
        byte[]? bytes;
        try
        {
            bytes = _file.ReadChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Usually another program mid-save, or a save that deletes the file before renaming the new one in.
            // Read again shortly; after a few tries, wait for the next file event.
            lock (_gate)
            {
                if (!_disposed && ++_retries <= MaxUnreadableRetries) _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
            }

            return;
        }

        if (bytes is not null) _ui.Post(_ => Take(bytes), null);
    }

    /// On the UI thread: the session takes the bytes, or, during a drag, takes them when it ends.
    void Take(byte[] bytes)
    {
        if (_disposed) return;
        if (_session.IsPreviewing || _session.IsInteracting)
        {
            _deferred = bytes;
            return;
        }

        _deferred = null;
        // Taken first: from here on the session accounts for these bytes, so autosave won't write text made before
        // over them, whether the session takes them as a step or pauses on them.
        _file.Accept(bytes);
        LayoutDocument document;
        try
        {
            document = LayoutDocument.FromBytes(bytes);
        }
        catch (LayoutEditException ex)
        {
            _session.PauseOnUnreadableFile(new LoadError(ex.Reason, ex.Line));
            return;
        }

        try
        {
            _session.Apply(new ReplaceDocument(document.Text, Label));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LayoutEditException)
        {
            // The theme's other files couldn't be read as it loaded, and the session is as it was. Its text isn't the
            // file's, so it pauses rather than let autosave write over the hand edit; the next change to the file is
            // read again.
            _session.PauseOnUnreadableFile(new LoadError(ex.Message, 0));
        }
    }

    /// On the UI thread: a drag that ended (on the canvas or in a panel) lets a change read during it in, posted so the
    /// session's other listeners hear of the drag's end first.
    void OnSessionChanged(object? sender, SessionChanges changes)
    {
        if (_deferred is not { } bytes || _session.IsPreviewing || _session.IsInteracting) return;
        _deferred = null;
        _ui.Post(_ => Take(bytes), null);
    }
}
