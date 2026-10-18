using System.Globalization;
using System.Security.AccessControl;
using System.Text;

namespace Marquee.Core.Logging;

/// Writes the log to one file per local day, marquee-yyyyMMdd.log, in the same `[HH:mm:ss] message` shape the console
/// shows, for the tray app, which has no console. Opening a day's file keeps `keepDays` files, that day's and the ones
/// of the days before it, and deletes older ones. A day's file stops at `maxFileBytes` with one line saying so, so a
/// provider stuck in a loop can't fill the disk. Every write is flushed and the file is shared for reading, so Notepad
/// can open the live log. Nothing here ever throws: a line that can't be written is dropped and the next line tries
/// again.
///
/// More than one process writes the same day's file (the tray all day, and any `marquee run` next to it), so the file
/// is opened for appending only: Windows then puts every write at the end of the file as it is at that moment, whoever
/// wrote last, and one line is one write. (.NET's FileMode.Append only seeks to the end when the file opens, and each
/// process would then write over the other's lines.)
public sealed class FileLog : ILog, IDisposable
{
    public const long DefaultMaxFileBytes = 10 * 1024 * 1024;
    public const int DefaultKeepDays = 7;

    const string Prefix = "marquee-";
    const string Extension = ".log";
    const string DateFormat = "yyyyMMdd";
    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    readonly string _folder;
    readonly Func<DateTime> _localNow;
    readonly long _maxFileBytes;
    readonly int _keepDays;
    readonly object _gate = new();
    FileStream? _file;
    DateTime _day;
    bool _full;
    bool _disposed;

    /// `localNow` is the local clock, there so tests can move it past midnight.
    public FileLog(string folder, Func<DateTime>? localNow = null, long maxFileBytes = DefaultMaxFileBytes,
        int keepDays = DefaultKeepDays)
    {
        _folder = folder;
        _localNow = localNow ?? (() => DateTime.Now);
        _maxFileBytes = maxFileBytes;
        _keepDays = keepDays;
    }

    public void Info(string message) => Write(message);
    public void Warn(string message) => Write($"Warning: {message}");
    public void Error(string message) => Write($"Error: {message}");

    void Write(string message)
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                var now = _localNow();
                if (_file is null || now.Date != _day) Open(now.Date);
                if (_full) return;

                var time = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                var line = Utf8NoBom.GetBytes($"[{time}] {message}{Environment.NewLine}");

                // The file as it is now, with whatever another process added since this one last wrote.
                var length = _file!.Length;
                if (length >= _maxFileBytes)
                {
                    // Another process reached the limit, and wrote the notice.
                    _full = true;
                    return;
                }

                if (length + line.Length > _maxFileBytes)
                {
                    _full = true;
                    var limit = (_maxFileBytes / (1024.0 * 1024.0)).ToString("0.##", CultureInfo.InvariantCulture);
                    line = Utf8NoBom.GetBytes(
                        $"[{time}] Warning: The log reached its {limit} MB limit for today; logging resumes tomorrow."
                        + Environment.NewLine);
                }
                _file.Write(line);
                _file.Flush();
            }
            catch (Exception)
            {
                // Logging must never take Marquee down. Drop this line and close the file, so the next line opens it
                // again (the folder may come back, the disk may get space).
                Close();
            }
        }
    }

    void Open(DateTime day)
    {
        Close();
        Directory.CreateDirectory(_folder);
        DeleteOldFiles(day);
        var path = Path.Combine(_folder, Prefix + day.ToString(DateFormat, CultureInfo.InvariantCulture) + Extension);
        // AppendData without WriteData: Windows writes at the end of the file, whatever position the stream keeps. No
        // buffer, so each line goes to the file in one write.
        _file = new FileInfo(path).Create(FileMode.OpenOrCreate, FileSystemRights.AppendData,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.None, fileSecurity: null);
        _day = day;
        // A restart on a day already at the limit stays quiet: the notice is in the file from the first run.
        _full = _file.Length >= _maxFileBytes;
    }

    /// Judges age by the date in the name, never by file times, which copying or editing a file changes. Files that
    /// don't match marquee-yyyyMMdd.log aren't Marquee's logs and are left alone.
    void DeleteOldFiles(DateTime today)
    {
        var oldestKept = today.AddDays(-(_keepDays - 1));
        foreach (var path in Directory.EnumerateFiles(_folder, Prefix + "*" + Extension))
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) continue;
            var date = name[Prefix.Length..^Extension.Length];
            if (!DateTime.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || day >= oldestKept) continue;
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Open in an editor, or read-only; the next day's open tries again.
            }
        }
    }

    void Close()
    {
        try
        {
            _file?.Dispose();
        }
        catch (Exception)
        {
            // Disposing flushes, which fails the same way the write did; the handle is released either way.
        }
        _file = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Close();
        }
    }
}
