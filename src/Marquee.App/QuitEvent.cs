using System.Security.AccessControl;
using System.Security.Principal;
using Marquee.App.Resources;
using Marquee.Core.Logging;

namespace Marquee.App;

/// The event the installer sets to make the tray quit (TrayNames.Quit): `marquee task stop`, run by the installer as
/// SYSTEM before it replaces or removes the tray's files, sets it (TrayStopper), and the tray quits as its Quit command
/// does, so the screen is let go of, the ring goes dark and the Claude limits are saved. A tray it can't reach this way
/// is ended instead, which does none of that.
///
/// Only SYSTEM and administrators may open the event; its user may not, so an unelevated program can't quit an
/// elevated tray at will. (An unelevated tray's user owns its event, and an owner may change who can set it; but that
/// user may end that tray's process anyway.) The tray waits on the handle it made the event with, which has every right
/// whatever the DACL says.
sealed class QuitEvent : IDisposable
{
    readonly EventWaitHandle _event;
    RegisteredWaitHandle? _waiting;

    QuitEvent(EventWaitHandle quit) => _event = quit;

    /// Makes the event, or returns null, with a warning in the log, when it exists already. The tray then can't be asked
    /// to quit, and the installer ends it instead (TrayStopper does when there is no event to set, or the tray doesn't
    /// quit in time).
    ///
    /// An event this process didn't make isn't waited on: whoever made it chose who may set it, and it may be anyone. It
    /// is either a tray that is still exiting (its event is gone with it, and the next start makes a new one) or a
    /// program squatting on the name to quit the tray whenever it likes; waiting on its event would let that program
    /// quit an elevated tray, which is just what the event's DACL is there to stop.
    public static QuitEvent? Create(string name, ILog log)
    {
        try
        {
            var quit = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out var createdNew, Security());
            if (createdNew) return new QuitEvent(quit);
            quit.Dispose();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // It exists with access this process lacks (an elevated tray's, seen from an unelevated one), or it isn't
            // an event.
        }

        log.Warn(Strings.QuitEventTaken(name));
        return null;
    }

    /// Calls `quit` on a thread-pool thread when the event is set, once: the tray is quitting by then.
    public void OnSet(Action quit) =>
        _waiting = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => quit(), null, Timeout.Infinite, executeOnlyOnce: true);

    public void Dispose()
    {
        _waiting?.Unregister(null);
        _event.Dispose();
    }

    /// Full control for SYSTEM and administrators, and nothing for anyone else. Protected, so nothing is inherited from
    /// the namespace's own DACL.
    static EventWaitHandleSecurity Security()
    {
        var security = new EventWaitHandleSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            EventWaitHandleRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            EventWaitHandleRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
