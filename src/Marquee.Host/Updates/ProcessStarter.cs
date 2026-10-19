using System.Diagnostics;

namespace Marquee.Host.Updates;

/// Starts a program and returns it without waiting for it. Tests put a fake in its place, so no test runs msiexec.
public interface IProcessStarter
{
    /// Starts `fileName` (a full path) with `arguments`, the command line as written, and returns it, to wait for its
    /// exit or not. Throws Win32Exception if it can't.
    IStartedProcess Start(string fileName, string arguments);
}

/// A process IProcessStarter started, held by its handle, so its exit code can still be read once it has exited.
/// Disposing lets go of the handle, not of the process, which runs on.
public interface IStartedProcess : IDisposable
{
    /// Completes with the process's exit code when it exits. Cancelling ends the waiting, not the process.
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
}

/// Starts the program directly, not through the shell, with this process's rights: the elevated tray starts msiexec
/// elevated, with no second UAC prompt.
public sealed class ProcessStarter : IProcessStarter
{
    // Without the shell, Start returns the process or throws; it is null only when the shell hands the file to a program
    // that is already running.
    public IStartedProcess Start(string fileName, string arguments) =>
        new StartedProcess(Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false })!);

    sealed class StartedProcess(Process process) : IStartedProcess
    {
        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }

        public void Dispose() => process.Dispose();
    }
}
