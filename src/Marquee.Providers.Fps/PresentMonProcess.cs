namespace Marquee.Providers.Fps;

/// One run of PresentMon and the frames it has reported. A thread reads its stdout: the first line is the CSV header,
/// and every row after it goes into Stats, stamped with the time it was read. Rows that can't be read are skipped. One
/// run's frames never mix with another's, because CPUStartTime counts from when that run started recording.
sealed class PresentMonProcess : IDisposable
{
    readonly IPresentMonRun _run;
    readonly Func<TimeSpan> _clock;
    volatile string? _readFailure;
    volatile bool _disposed;

    /// Takes the run over: disposing this stops it. `clock` is the provider's monotonic clock.
    public PresentMonProcess(IPresentMonRun run, Func<TimeSpan> clock)
    {
        _run = run;
        _clock = clock;
        new Thread(Read) { IsBackground = true, Name = "Marquee PresentMon reader" }.Start();
    }

    public FrameStats Stats { get; } = new();

    /// Why this run is over, or null while it is going: it exited, or its output isn't a CSV Marquee can read.
    public string? Failure
    {
        get
        {
            if (_readFailure is { } failure) return failure;
            if (_run.ExitCode is not { } code) return null;
            return _run.LastError is { } error
                ? $"PresentMon exited with code {code} (its last error: {error})."
                : $"PresentMon exited with code {code}.";
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _run.Dispose();
    }

    void Read()
    {
        try
        {
            if (_run.Output.ReadLine() is not { } first) return;
            var header = PresentMonCsv.ParseHeader(first);
            while (_run.Output.ReadLine() is { } line)
            {
                if (PresentMonCsv.TryParseRow(line, header, out var frame))
                    Stats.Add(frame, _clock());
            }
        }
        catch (Exception ex)
        {
            // Nothing above this thread would catch it, and an exception escaping a thread ends Marquee. After Dispose,
            // an error is only the output being closed under the read.
            if (!_disposed) _readFailure = ex is FormatException ? ex.Message : $"Reading PresentMon's output failed: {ex.Message}";
        }
    }
}
