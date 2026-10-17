using Marquee.Core.Logging;

namespace Marquee.Core.Tests.TestSupport;

/// Collects log lines as "info: …", "warn: …", "error: …".
sealed class ListLog : ILog
{
    readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries) return _entries.ToList();
        }
    }

    public void Info(string message) => Add("info", message);
    public void Warn(string message) => Add("warn", message);
    public void Error(string message) => Add("error", message);

    void Add(string level, string message)
    {
        lock (_entries) _entries.Add($"{level}: {message}");
    }
}
