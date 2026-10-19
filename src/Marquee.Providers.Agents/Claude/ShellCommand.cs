using System.Diagnostics;

namespace Marquee.Providers.Agents.Claude;

/// What a command printed and how it ended. TimedOut means it was stopped (with its child processes) at the timeout.
public sealed record ShellResult(int ExitCode, byte[] Output, bool TimedOut);

/// A shell that runs a command line: the shell's path, then the arguments that come before the command.
public sealed record ShellCommand(string FileName, IReadOnlyList<string> Arguments)
{
    public static ShellCommand Bash(string path) => new(path, ["-c"]);

    public static ShellCommand PowerShell { get; } = new("powershell.exe", ["-NoProfile", "-Command"]);

    /// Runs the command with `stdin` as its input and returns its output bytes unchanged (colors included).
    /// Its stderr goes to this process's stderr. Throws if the shell can't be started.
    public ShellResult Run(string command, byte[] stdin, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(FileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in Arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add(command);

        // The deadline runs from here: the whole call, waiting for output included, takes about `timeout` at most.
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{FileName} did not start");
        var output = new OutputBuffer();
        var reading = PipeWork.Start(() => output.ReadAll(process.StandardOutput.BaseStream));
        // On its own task: a command that never reads its input must not block this one once the pipe buffer fills.
        _ = PipeWork.Start(() =>
        {
            try
            {
                process.StandardInput.BaseStream.Write(stdin);
                process.StandardInput.Close();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The command exited without reading all of its input.
            }
        });

        if (!process.WaitForExit(timeout))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.ComponentModel.Win32Exception)
            {
                // It exited just now, or part of its tree can't be killed; either way it is being abandoned.
            }

            reading.Wait(TimeSpan.FromSeconds(1));
            return new ShellResult(-1, output.Snapshot(), TimedOut: true);
        }

        // Output ends only when every process holding the pipe has closed it, and a background child can hold it long
        // after the command has finished. Give the reader until the deadline (at least a moment to drain what the
        // command wrote), then settle for what has arrived: the command itself finished.
        var remaining = timeout - clock.Elapsed;
        reading.Wait(remaining > MinimumDrain ? remaining : MinimumDrain);
        return new ShellResult(process.ExitCode, output.Snapshot(), TimedOut: false);
    }

    static readonly TimeSpan MinimumDrain = TimeSpan.FromMilliseconds(250);

    /// Output collected on a reader task, safe to copy at any moment.
    sealed class OutputBuffer
    {
        readonly object gate = new();
        readonly MemoryStream bytes = new();

        public void ReadAll(Stream stream)
        {
            var chunk = new byte[8192];
            try
            {
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    lock (gate) bytes.Write(chunk, 0, read);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The pipe was closed under us (the process was abandoned); keep what arrived.
            }
        }

        public byte[] Snapshot()
        {
            lock (gate) return bytes.ToArray();
        }
    }
}
