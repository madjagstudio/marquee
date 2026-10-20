using System.Runtime.InteropServices;

namespace Marquee.Host.SensorFeed;

/// The Win32 call behind telling a pipe that doesn't exist from one that is busy.
static class PipeNative
{
    const int ERROR_FILE_NOT_FOUND = 2;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WaitNamedPipeW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WaitNamedPipe(string name, uint timeout);

    /// Whether any instance of the local pipe `name` exists, free or busy, waiting at most a millisecond. WaitNamedPipe
    /// fails with ERROR_FILE_NOT_FOUND only when there is none, and it never connects, so asking takes no connection
    /// from the server. (File.Exists on \\.\pipe\name would open the pipe, and use up a connection.)
    public static bool Exists(string name) =>
        WaitNamedPipe($@"\\.\pipe\{name}", 1) || Marshal.GetLastPInvokeError() != ERROR_FILE_NOT_FOUND;
}
