using System.Diagnostics;
using System.Runtime.InteropServices;
using Marquee.App.Resources;

namespace Marquee.App;

/// Opens folders, files and web pages through Explorer, so they open as the user and not elevated: the tray runs
/// elevated, and Process.Start(file) with UseShellExecute would start the user's editor (or browser) elevated too, with
/// every file it saves or plug-in it loads running as administrator.
///
/// explorer.exe started by an elevated process doesn't stay elevated: when the desktop's Explorer is already running
/// (it always is while a user is signed in), the new explorer.exe hands the path to it and exits, and the desktop's
/// Explorer, which is unelevated, opens the folder, or opens the file with its default app as a double-click would.
/// This is long-standing Explorer behavior rather than a documented promise; the documented route (asking the
/// desktop's IShellDispatch2 to ShellExecute) is the fallback if it ever stops working.
///
/// `hasDesktopShell` is whether the desktop's Explorer is there to hand the path to (GetShellWindow); tests give their
/// own.
sealed class ExplorerShell(Func<bool>? hasDesktopShell = null) : IShell
{
    readonly Func<bool> _hasDesktopShell = hasDesktopShell ?? (() => GetShellWindow() != IntPtr.Zero);

    /// Throws InvalidOperationException, opening nothing, when there is no desktop shell: after Explorer crashed and
    /// before it restarts, or under another shell, explorer.exe has no one to hand the path to and may open it itself,
    /// elevated. The tray shows the message in a balloon.
    public void Open(string path)
    {
        if (!_hasDesktopShell()) throw new InvalidOperationException(Strings.NoDesktopShell);

        // By its full path: an elevated process must never let the search path pick which explorer.exe runs.
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var start = new ProcessStartInfo(explorer, Arguments(path)) { UseShellExecute = false };
        using var process = Process.Start(start);
    }

    /// explorer.exe's command line for `path`: always quoted, since Explorer reads a comma as the end of a path; never
    /// with a trailing backslash, which would escape the closing quote. Windows paths can't hold quotes, and a URL's
    /// AbsoluteUri escapes them.
    internal static string Arguments(string path) => $"\"{Path.TrimEndingDirectorySeparator(path)}\"";

    /// The desktop's shell window, or zero when no shell is running.
    [DllImport("user32.dll")]
    static extern IntPtr GetShellWindow();
}
