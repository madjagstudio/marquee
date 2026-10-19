using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Marquee.Providers.Fps;

/// The process whose window is in front: the game, when one is being played.
public static class ForegroundProcess
{
    /// The id of the process that owns the foreground window, or null when no window has the focus (while one is being
    /// closed, say, or the desktop is locked).
    public static int? Id()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        return GetWindowThreadProcessId(window, out var processId) != 0 && processId != 0 ? (int)processId : null;
    }

    /// The process's name without ".exe" (what Task Manager's Details tab shows, less the extension), or null when it
    /// has exited. Reading the name needs no handle to the process, so it works for elevated and protected ones too.
    public static string? Name(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
