using System.ComponentModel;
using System.Diagnostics;
using Marquee.Host.Resources;

namespace Marquee.Host;

/// How schtasks ended: its exit code and everything it printed, stdout then stderr (its errors go to stderr).
public sealed record SchTasksResult(int ExitCode, string Output);

/// Runs schtasks.exe with the given arguments. Tests put a fake in its place, so no test touches the real Task Scheduler.
public interface ISchTasks
{
    SchTasksResult Run(IReadOnlyList<string> arguments);
}

/// The real schtasks.exe, by its full path in System32: an elevated Marquee must never start whatever a writable folder
/// on the PATH calls schtasks.
public sealed class SchTasks(TimeSpan timeout) : ISchTasks
{
    public SchTasks() : this(TimeSpan.FromMinutes(1))
    {
    }

    public static string ExePath => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    /// Throws TimeoutException if schtasks hasn't finished within the timeout (it is stopped), Win32Exception if it
    /// can't be started.
    public SchTasksResult Run(IReadOnlyList<string> arguments)
    {
        // No shell and no window; the arguments are passed one by one, so a path with spaces needs no quoting here. The
        // output is decoded with the console's code page, which is what schtasks writes when its output is redirected.
        var start = new ProcessStartInfo(ExePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new Win32Exception(Strings.SchTasksDidNotStart);
        // Both pipes are read at once, each on a thread of its own rather than the thread pool's, so neither can fill
        // up and stall schtasks, and neither read waits for a pool thread on a busy PC.
        var output = ReadOnOwnThread(process.StandardOutput);
        var errors = ReadOnOwnThread(process.StandardError);

        if (!process.WaitForExit(timeout))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or AggregateException)
            {
                // It exited just now; either way it is being abandoned.
            }

            throw new TimeoutException(Strings.SchTasksTimedOut((int)timeout.TotalSeconds));
        }

        // schtasks has exited, so both pipes are at their end and the reads finish.
        return new SchTasksResult(process.ExitCode, output.Result + errors.Result);
    }

    static Task<string> ReadOnOwnThread(StreamReader reader) =>
        Task.Factory.StartNew(reader.ReadToEnd, CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
}
