using System.Runtime.InteropServices;
using Marquee.Editor.Model;

namespace Marquee.Editor.Startup;

/// "Bring the window forward". One editor has a theme folder open (ThemeLock), so opening the same theme again asks
/// that editor to show its window, and goes no further. The editor holding the theme waits on a named event, named
/// after its lock's mutex (`Local\Marquee.Editor.<hash>.Show`, in the user's logon session); the second one sets it.
///
/// Windows lets only the program the user last used take the foreground, which is the second editor, just started;
/// it lets any process take it before setting the event (AllowSetForegroundWindow), so the first one's window can
/// come forward rather than flash in the taskbar.
public sealed class WindowSignal : IDisposable
{
    /// How long Send keeps trying while the editor that has the theme is still starting: it takes the lock before it
    /// listens.
    public static readonly TimeSpan SendPatience = TimeSpan.FromSeconds(3);

    const int AsfwAny = -1;

    readonly EventWaitHandle _event;
    readonly RegisteredWaitHandle _wait;

    WindowSignal(EventWaitHandle signal, RegisteredWaitHandle wait)
    {
        _event = signal;
        _wait = wait;
    }

    /// The event's name for `themeFolder`.
    public static string NameFor(string themeFolder) => ThemeIdentity.MutexName(themeFolder) + ".Show";

    /// Listens for `themeFolder`'s signal until disposed, posting `show` to `ui` each time it comes. Null when the name
    /// is held by something this user can't open: the editor works on, and a second one says the theme is open
    /// already instead of bringing this window forward.
    public static WindowSignal? Listen(string themeFolder, SynchronizationContext ui, Action show)
    {
        EventWaitHandle signal;
        try
        {
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, NameFor(themeFolder));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return null;
        }

        var wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => ui.Post(_ => show(), null), null,
            Timeout.Infinite, executeOnlyOnce: false);
        return new WindowSignal(signal, wait);
    }

    /// Asks the editor that has `themeFolder` open to show its window. False when none answered within `patience`
    /// (SendPatience by default): it is closing, or stuck.
    public static bool Send(string themeFolder, TimeSpan? patience = null)
    {
        var until = DateTime.UtcNow + (patience ?? SendPatience);
        while (true)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(NameFor(themeFolder), out var signal))
                {
                    using (signal)
                    {
                        AllowSetForegroundWindow(AsfwAny);
                        return signal.Set();
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Not an event this user may set: there is no editor of theirs to ask.
                return false;
            }

            if (DateTime.UtcNow >= until) return false;
            Thread.Sleep(100);
        }
    }

    public void Dispose()
    {
        _wait.Unregister(null);
        _event.Dispose();
    }

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
