namespace Marquee.Core.Themes;

/// Watches a theme folder and reloads it after any file in it changes (debounced, since editors save in bursts).
/// A good load goes to `loaded`; a bad one goes to `failed` and nothing else happens, so whatever is on screen,
/// the last good layout, stays there. Callbacks run on a thread-pool thread.
public sealed class ThemeWatcher : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);
    const int MaxLockedRetries = 3;

    readonly string _folder;
    readonly Action<LoadedTheme> _loaded;
    readonly Action<ThemeException> _failed;
    readonly TimeSpan _debounce;
    readonly int? _fpsOverride;
    readonly Timer _timer;
    readonly object _gate = new();
    FileSystemWatcher? _watcher;
    int _lockedRetries;
    bool _disposed;

    /// `fpsOverride` is `--fps`, passed to ThemeLoader.Load.
    public ThemeWatcher(string folder, Action<LoadedTheme> loaded, Action<ThemeException> failed, TimeSpan? debounce = null,
        int? fpsOverride = null)
    {
        _folder = folder;
        _fpsOverride = fpsOverride;
        _loaded = loaded;
        _failed = failed;
        _debounce = debounce ?? DefaultDebounce;
        _timer = new Timer(_ => ReloadFromTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// Loads the theme now and reports it through the callbacks. True if it loaded. With `retryIfLocked` false (the
    /// startup load), a locked or unreadable file is reported through `failed` at once instead of being retried.
    public bool Reload(bool retryIfLocked = true)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            LoadedTheme theme;
            try
            {
                theme = ThemeLoader.Load(_folder, _fpsOverride);
            }
            catch (ThemeException ex)
            {
                _lockedRetries = 0;
                _failed(ex);
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Usually the editor still has the file open mid-save. Try again shortly before calling it an error.
                if (retryIfLocked && ++_lockedRetries <= MaxLockedRetries)
                {
                    Schedule();
                    return false;
                }

                _lockedRetries = 0;
                _failed(Unreadable(ex));
                return false;
            }
            catch (Exception ex)
            {
                // Anything else (a path the OS rejects, a codec bug) is still just a broken theme, not a crash.
                _lockedRetries = 0;
                _failed(Unloadable(ex));
                return false;
            }

            _lockedRetries = 0;
            _loaded(theme);
            return true;
        }
    }

    /// Loads a theme once, outside any watcher, the way Reload's startup load does: every failure, a locked file
    /// included, is a ThemeException with a message for the user. For a host that must know a new theme loads before it
    /// lets go of the one on screen.
    public static LoadedTheme Load(string folder, int? fpsOverride = null)
    {
        try
        {
            return ThemeLoader.Load(folder, fpsOverride);
        }
        catch (ThemeException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Unreadable(ex);
        }
        catch (Exception ex)
        {
            throw Unloadable(ex);
        }
    }

    static ThemeException Unreadable(Exception ex) => new($"Could not read the theme: {ex.Message}");

    static ThemeException Unloadable(Exception ex) => new($"Could not load the theme: {ex.Message}");

    public void Start()
    {
        if (_watcher is not null) return;
        _watcher = new FileSystemWatcher(_folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += (_, _) => Schedule();
        _watcher.Created += (_, _) => Schedule();
        _watcher.Deleted += (_, _) => Schedule();
        _watcher.Renamed += (_, _) => Schedule();
        _watcher.Error += (_, _) => Schedule();
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer.Dispose();
        lock (_gate) _disposed = true;
    }

    void ReloadFromTimer()
    {
        try
        {
            Reload();
        }
        catch (Exception ex)
        {
            // This runs on a thread-pool thread, where an unhandled exception ends the process: a throwing
            // `loaded`/`failed` callback must not take the process down. Load failures already reached `failed`
            // inside Reload, so this is a callback failing; make one best-effort report so the reload isn't silent.
            try { _failed(new ThemeException($"Could not apply the theme: {ex.Message}")); } catch { /* a failing callback must not take the process down */ }
        }
    }

    void Schedule()
    {
        try
        {
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // A last file event raced with Dispose.
        }
    }
}
