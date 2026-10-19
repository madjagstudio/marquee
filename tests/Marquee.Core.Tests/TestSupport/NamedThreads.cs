using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Marquee.Core.Tests.TestSupport;

/// Counts this process's threads by name. .NET gives a named thread's name to Windows as the thread's description,
/// which, unlike a managed Thread, can be listed from outside the code that started it.
static class NamedThreads
{
    const uint ThreadQueryLimitedInformation = 0x0800;

    public static int Count(string name)
    {
        var count = 0;
        using var process = Process.GetCurrentProcess();
        foreach (ProcessThread thread in process.Threads)
        {
            var handle = OpenThread(ThreadQueryLimitedInformation, false, (uint)thread.Id);
            if (handle == IntPtr.Zero) continue; // it ended since the list was taken
            try
            {
                if (GetThreadDescription(handle, out var description) < 0 || description == IntPtr.Zero) continue;
                if (Marshal.PtrToStringUni(description) == name) count++;
                LocalFree(description);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        return count;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern IntPtr OpenThread(uint access, bool inheritHandle, uint threadId);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int GetThreadDescription(IntPtr thread, out IntPtr description);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern bool CloseHandle(IntPtr handle);
}
