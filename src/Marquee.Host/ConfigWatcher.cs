using Marquee.Core.Config;
using Marquee.Host.Resources;

namespace Marquee.Host;

/// Watches config.yaml and reads it again after it changes (debounced, like ThemeWatcher, since editors save in
/// bursts). A good file goes to `changed`, a missing one as the defaults; a broken one goes to `failed`, and the caller
/// keeps the last good config. Callbacks run on a thread-pool thread, one at a time.
///
/// It watches the file's folder rather than the file, so a save that writes a temporary file and renames it over
/// config.yaml is seen, and so is a config.yaml created after the watch began. The folder must exist when Start is
/// called: the watcher never creates it (`marquee run` makes %LOCALAPPDATA%\Marquee before it starts watching).
public sealed class ConfigWatcher : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);
    const int MaxLockedRetries = 3;

    readonly string _path;
    readonly string _folder;
    readonly string _fileName;
    readonly Action<MarqueeConfig> _changed;
    readonly Action<ConfigException> _failed;
    readonly TimeSpan _debounce;
    readonly Timer _timer;
    readonly object _gate = new();
    FileSystemWatcher? _watcher;
    int _lockedRetries;
    bool _disposed;

    public ConfigWatcher(string path, Action<MarqueeConfig> changed, Action<ConfigException> failed, TimeSpan? debounce = null)
    {
        _path = Path.GetFullPath(path);
        _folder = Path.GetDirectoryName(_path)!;
        _fileName = Path.GetFileName(_path);
        _changed = changed;
        _failed = failed;
        _debounce = debounce ?? DefaultDebounce;
        _timer = new Timer(_ => ReloadFromTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// Starts watching. False, with nothing watched, if the file's folder doesn't exist.
    public bool Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watcher is not null) return true;
            if (!Directory.Exists(_folder)) return false;

            // No Filter: a rename is matched against both of its names here, which a filter doesn't promise.
            var watcher = new FileSystemWatcher(_folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, e) => ScheduleIf(e.Name);
            watcher.Created += (_, e) => ScheduleIf(e.Name);
            watcher.Deleted += (_, e) => ScheduleIf(e.Name);
            watcher.Renamed += (_, e) =>
            {
                ScheduleIf(e.Name);
                ScheduleIf(e.OldName);
            };
            watcher.Error += (_, _) => Schedule(); // events were lost; read the file again to be sure
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _watcher?.Dispose();
            _timer.Dispose();
        }
    }

    void ScheduleIf(string? name)
    {
        if (string.Equals(name, _fileName, StringComparison.OrdinalIgnoreCase)) Schedule();
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

    void ReloadFromTimer()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                Reload();
            }
            catch (Exception ex)
            {
                // This runs on a thread-pool thread, where an unhandled exception ends the process: a throwing
                // `changed` callback must not take the process down. Make one best-effort report so it isn't silent.
                try { _failed(new ConfigException(Strings.CouldNotApplyConfig(ex.Message))); } catch { /* a failing callback must not take the process down */ }
            }
        }
    }

    void Reload()
    {
        MarqueeConfig config;
        try
        {
            config = ConfigFile.Read(_path);
        }
        catch (ConfigException ex)
        {
            _lockedRetries = 0;
            _failed(ex);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Usually the editor still has the file open mid-save. Try again shortly before calling it an error.
            if (++_lockedRetries <= MaxLockedRetries)
            {
                Schedule();
                return;
            }

            _lockedRetries = 0;
            _failed(new ConfigException(Strings.CouldNotReadConfig(ex.Message)));
            return;
        }

        _lockedRetries = 0;
        _changed(config);
    }
}
