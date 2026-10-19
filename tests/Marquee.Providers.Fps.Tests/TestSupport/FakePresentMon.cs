using System.Collections.Concurrent;
using Marquee.Providers.Fps;

namespace Marquee.Providers.Fps.Tests.TestSupport;

/// A PresentMon run with no process: the test writes its CSV lines, and says when it exits and what it last wrote to
/// stderr.
sealed class FakePresentMon : IPresentMonRun
{
    readonly LineFeed _output = new();
    readonly object _gate = new();
    volatile string? _lastError;
    volatile bool _disposed;
    int? _exitCode;

    public TextReader Output => _output;

    public int? ExitCode
    {
        get
        {
            lock (_gate) return _exitCode;
        }
    }

    public string? LastError => _lastError;

    public bool Disposed => _disposed;

    public void Write(params string[] lines) => _output.Add(lines);

    public void Write(IEnumerable<string> lines) => _output.Add(lines);

    /// Waits until the reader has handled every line written so far and is waiting for the next.
    public bool WaitUntilRead() => Wait.Until(() => _output.AllRead);

    /// Its output ends, then it reports the exit code.
    public void Exit(int code, string? lastError = null)
    {
        _lastError = lastError;
        _output.End();
        lock (_gate) _exitCode = code;
    }

    public void Dispose()
    {
        _disposed = true;
        _output.End();
    }

    /// Lines handed to ReadLine one at a time, blocking until the test writes more or ends the output.
    sealed class LineFeed : TextReader
    {
        readonly BlockingCollection<string> _lines = new();
        int _added;
        int _requested;

        /// A line is handled once the reader asks for the one after it, so asking for one more than were written means
        /// every written line has been handled.
        public bool AllRead => Volatile.Read(ref _requested) > Volatile.Read(ref _added);

        public void Add(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                Interlocked.Increment(ref _added);
                _lines.Add(line);
            }
        }

        public void End() => _lines.CompleteAdding();

        public override string? ReadLine()
        {
            Interlocked.Increment(ref _requested);
            try
            {
                return _lines.TryTake(out var line, System.Threading.Timeout.Infinite) ? line : null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }
    }
}
