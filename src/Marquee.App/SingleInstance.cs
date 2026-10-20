using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Marquee.App;

/// One tray per user (TrayNames.Instance), and a way for a second start to open the first one's menu instead
/// (TrayNames.ShowMenu): the first waits on a named event, which the second sets before it exits.
///
/// The logon task starts the tray elevated, but a user who starts MarqueeTray.exe again from the Start menu or
/// Explorer starts it unelevated. Objects an elevated process makes only let administrators in by default, so the
/// unelevated start can't open the tray's mutex (which still tells it a tray is running) and could not set the event,
/// which therefore lets the user set it. Setting it can do no more than open the menu.
sealed class SingleInstance : IDisposable
{
    const int ASFW_ANY = -1;

    readonly Mutex _instance;
    readonly EventWaitHandle? _show;
    RegisteredWaitHandle? _waiting;

    SingleInstance(Mutex instance, EventWaitHandle? show)
    {
        _instance = instance;
        _show = show;
    }

    /// This process becomes the tray, or null when another one is (including one this process may not open: an
    /// elevated tray, seen from an unelevated start). Only the mutex's existence counts, not who owns it, so it needn't
    /// be released on the thread that made it.
    public static SingleInstance? TryClaim(string instanceName, string showName)
    {
        Mutex instance;
        try
        {
            instance = new Mutex(initiallyOwned: false, instanceName, out var createdNew);
            if (!createdNew)
            {
                instance.Dispose();
                return null;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return new SingleInstance(instance, ShowEvent(showName));
    }

    /// Asks the running tray to open its menu. False when there is none, or it can't be reached.
    public static bool AskFirstToShow(string showName)
    {
        // Windows only lets a process bring its window to the front when the user is working with it, which is this
        // newly started process, not the tray; this passes that right on, so the tray's menu opens in front.
        AllowSetForegroundWindow(ASFW_ANY);
        try
        {
            if (!EventWaitHandleAcl.TryOpenExisting(showName, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, out var show))
                return false;
            using (show) return show.Set();
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// Calls `show` on a thread-pool thread each time another start asks. Call it once.
    public void OnShowRequested(Action show)
    {
        if (_show is null) return;
        _waiting = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _waiting?.Unregister(null);
        _show?.Dispose();
        _instance.Dispose();
    }

    /// The show event, which the user may set and wait on, and administrators and SYSTEM may do anything with. Null if
    /// it can't be made: an old one left open by a start that is still exiting, made with other access. The tray
    /// still runs; only a second start can't open its menu.
    static EventWaitHandle? ShowEvent(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(identity.User!,
            EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, AccessControlType.Allow));
        security.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            EventWaitHandleRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            EventWaitHandleRights.FullControl, AccessControlType.Allow));
        try
        {
            return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out _, security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool AllowSetForegroundWindow(int processId);
}
