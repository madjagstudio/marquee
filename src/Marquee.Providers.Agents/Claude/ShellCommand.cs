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

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{FileName} did not start");
        var output = new MemoryStream();
        var reading = process.StandardOutput.BaseStream.CopyToAsync(output);
        // On its own task: a command that never reads its input must not block this one once the pipe buffer fills.
        _ = Task.Run(() =>
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
            catch (InvalidOperationException)
            {
                // It exited just now.
            }

            reading.Wait(TimeSpan.FromSeconds(1));
            return new ShellResult(-1, output.ToArray(), TimedOut: true);
        }

        reading.Wait();
        return new ShellResult(process.ExitCode, output.ToArray(), TimedOut: false);
    }
}
