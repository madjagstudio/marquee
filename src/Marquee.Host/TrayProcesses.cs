using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Marquee.Host;

/// The trays running on this PC from one exe, in every session: found by name, then kept only if their image is that
/// exe, so a tray run from a build folder is left alone. Each is held by a process handle from the moment it is found,
/// so a process ID reused after it exits can never lead to another process being ended. Seeing other users' processes
/// and their tokens takes SYSTEM or an administrator.
public sealed class TrayProcesses : ITrayProcesses
{
    const uint PROCESS_TERMINATE = 0x0001;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint SYNCHRONIZE = 0x00100000;
    const uint TOKEN_QUERY = 0x0008;
    const uint WAIT_OBJECT_0 = 0;

    /// How long Kill waits for the process to be gone, so its files are free when the installer reaches them.
    static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

    public IReadOnlyList<ITray> Running(string trayExePath)
    {
        var path = Path.GetFullPath(trayExePath);
        var trays = new List<ITray>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            using (process)
            {
                var handle = OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, false, process.Id);
                if (handle.IsInvalid || !string.Equals(ImagePath(handle), path, StringComparison.OrdinalIgnoreCase))
                {
                    handle.Dispose();
                    continue;
                }

                trays.Add(new Tray(handle, process.Id, process.SessionId, UserOf(handle)));
            }
        }

        return trays;
    }

    static string? ImagePath(SafeProcessHandle handle)
    {
        var buffer = new StringBuilder(1024);
        var length = buffer.Capacity;
        return QueryFullProcessImageName(handle, 0, buffer, ref length) ? buffer.ToString(0, length) : null;
    }

    /// The SID of the user the process runs as, or null when its token can't be read.
    static string? UserOf(SafeProcessHandle handle)
    {
        if (!OpenProcessToken(handle, TOKEN_QUERY, out var token)) return null;
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return identity.User?.Value;
    }

    sealed class Tray(SafeProcessHandle handle, int processId, int sessionId, string? userSid) : ITray
    {
        public int ProcessId => processId;

        public bool AskToQuit()
        {
            if (userSid is null) return false;
            try
            {
                if (!EventWaitHandle.TryOpenExisting(TrayNames.QuitIn(sessionId, userSid), out var quit)) return false;
                using (quit) return quit.Set();
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool WaitForExit(TimeSpan timeout) =>
            WaitForSingleObject(handle, (uint)timeout.TotalMilliseconds) == WAIT_OBJECT_0;

        public void Kill()
        {
            if (!TerminateProcess(handle, 1))
            {
                // Read before anything else can set it: the wait below makes a call of its own.
                var error = Marshal.GetLastWin32Error();
                // Failing because it has just exited is success.
                if (!WaitForExit(TimeSpan.Zero)) throw new Win32Exception(error);
            }

            WaitForExit(KillWait);
        }

        public void Dispose() => handle.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder name, ref int size);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
