using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Marquee.Core.Processes;

namespace Marquee.Providers.Fps;

/// PresentMon running as Marquee's child, in a ChildProcessJob of its own so that it dies with Marquee even if Marquee
/// crashes. Its stdout is read by PresentMonProcess; its stderr is read here, keeping the last line for the log.
sealed class PresentMonChild : IPresentMonRun
{
    /// How long ExitCode waits, once the process has exited, for its last stderr line to be read.
    static readonly TimeSpan ErrorDrain = TimeSpan.FromSeconds(1);

    readonly Process _process;
    readonly ChildProcessJob _job;
    readonly ManualResetEventSlim _errorsRead = new();
    volatile string? _lastError;

    PresentMonChild(Process process, ChildProcessJob job)
    {
        _process = process;
        _job = job;
        ProcessId = process.Id;
        Output = process.StandardOutput;
        Errors = process.StandardError; // taken here, so the thread never touches the Process object
        new Thread(() => ReadErrors(Errors)) { IsBackground = true, Name = "Marquee PresentMon stderr" }.Start();
    }

    public int ProcessId { get; }

    public TextReader Output { get; }

    /// Its stderr, which a thread of its own reads.
    internal StreamReader Errors { get; }

    public int? ExitCode
    {
        get
        {
            if (!_process.HasExited) return null;
            _errorsRead.Wait(ErrorDrain);
            return _process.ExitCode;
        }
    }

    public string? LastError => _lastError;

    /// Starts `exe` with the arguments, no window, stdout and stderr redirected, in its own folder, and puts it in a new
    /// job. Throws if it can't start, or can't be put in the job (it is killed first, so it never runs unguarded).
    public static IPresentMonRun Start(string exe, IReadOnlyList<string> arguments)
    {
        var job = new ChildProcessJob(); // first, so a job that can't be made starts nothing
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);

            process = Process.Start(start) ?? throw new InvalidOperationException($"{Path.GetFileName(exe)} did not start.");
            job.Assign(process.SafeHandle);
            return new PresentMonChild(process, job);
        }
        catch
        {
            if (process is not null) Kill(process);
            process?.Dispose();
            job.Dispose();
            throw;
        }
    }

    /// Kills it, and closing the job ends anything it started. Then closes this end of its stdout and stderr pipes,
    /// which Process.Dispose leaves to whoever read from them.
    public void Dispose()
    {
        Kill(_process);
        _process.Dispose();
        _job.Dispose();

        // With the processes gone the pipes are at their end, so a read still under way on the stdout reader's thread
        // returns rather than holding up the close; PresentMonProcess ignores what that read makes of the close. The
        // stderr thread catches less, so its reader is closed only once that thread is done (it is at the end of the
        // pipe too, so this is quick); if it somehow isn't, the reader is left to the finalizer.
        Output.Dispose();
        if (_errorsRead.Wait(ErrorDrain)) Errors.Dispose();
    }

    void ReadErrors(StreamReader errors)
    {
        try
        {
            while (errors.ReadLine() is { } line)
                if (!string.IsNullOrWhiteSpace(line)) _lastError = line.Trim();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Dispose closed the pipe.
        }
        finally
        {
            _errorsRead.Set();
        }
    }

    static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            // It exited just now, or part of its tree can't be killed; closing the job ends the rest.
        }
    }
}
