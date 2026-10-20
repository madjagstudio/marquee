using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Marquee.App.Resources;
using Marquee.App.Tests.TestSupport;
using Marquee.Host;

namespace Marquee.App.Tests;

/// The event the installer sets to make the tray quit. Each test makes a name of its own, so a real tray running on this
/// PC and the other tests are never involved; the installer's side is TrayStopper's own code.
public class QuitEventTests
{
    const string Tray = @"C:\Program Files\Marquee\MarqueeTray.exe";

    readonly ListLog _log = new();

    static string UniqueName(out string id)
    {
        id = $"Marquee-Test-Tray-Quit-{Guid.NewGuid():N}";
        return $@"Local\{id}";
    }

    static string UniqueName() => UniqueName(out _);

    /// Lets this process set the event as the installer, running as SYSTEM, may. A test runs as the user, whom the
    /// event's DACL leaves out; but the event's owner may always change its DACL, and that is the user when the test is
    /// unelevated, or administrators, which the test is then a member of, when it is elevated.
    static void ActAsInstaller(string name)
    {
        using var quit = EventWaitHandleAcl.OpenExisting(name, EventWaitHandleRights.ReadPermissions | EventWaitHandleRights.ChangePermissions);
        using var current = WindowsIdentity.GetCurrent();
        var security = quit.GetAccessControl();
        security.AddAccessRule(new EventWaitHandleAccessRule(current.User!,
            EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, AccessControlType.Allow));
        quit.SetAccessControl(security);
    }

    [Fact]
    public void Setting_the_event_quits_the_tray_once()
    {
        var name = UniqueName();
        using var quit = QuitEvent.Create(name, _log);
        Assert.NotNull(quit);
        var quits = 0;
        using var quitting = new SemaphoreSlim(0);
        quit.OnSet(() =>
        {
            Interlocked.Increment(ref quits);
            quitting.Release();
        });
        ActAsInstaller(name);

        // The tray waits on the handle it made the event with, which has every right although the DACL gives its user
        // none.
        Assert.True(TrayStopper.AskToQuit(name));
        Assert.True(quitting.Wait(Wait.Timeout), "The tray never heard the installer.");

        // The tray is quitting by then; asking again changes nothing.
        Assert.True(TrayStopper.AskToQuit(name));
        Assert.False(quitting.Wait(TimeSpan.FromMilliseconds(300)));
        Assert.Equal(1, Volatile.Read(ref quits));
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public void Only_SYSTEM_and_administrators_may_set_it()
    {
        var name = UniqueName();
        using var quit = QuitEvent.Create(name, _log);
        Assert.NotNull(quit);
        using var opened = EventWaitHandleAcl.OpenExisting(name, EventWaitHandleRights.ReadPermissions);

        var security = opened.GetAccessControl();
        var rules = security
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<EventWaitHandleAccessRule>()
            .Select(rule => (Who: (SecurityIdentifier)rule.IdentityReference, rule.AccessControlType, rule.EventWaitHandleRights))
            .OrderBy(rule => rule.Who.Value, StringComparer.Ordinal)
            .ToList();

        // Nothing for the user, and nothing inherited that could give the user something.
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(
        [
            (new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), AccessControlType.Allow, EventWaitHandleRights.FullControl),
            (new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), AccessControlType.Allow, EventWaitHandleRights.FullControl),
        ], rules);

        // Elevated, the test is an administrator, which may.
        if (!Environment.IsPrivilegedProcess)
            Assert.Throws<UnauthorizedAccessException>(() => EventWaitHandleAcl.OpenExisting(name, EventWaitHandleRights.Modify));
    }

    [Fact]
    public void An_event_another_program_made_first_is_not_waited_on_and_is_logged()
    {
        var name = UniqueName();
        using var squatter = new EventWaitHandle(false, EventResetMode.AutoReset, name);

        Assert.Null(QuitEvent.Create(name, _log));
        Assert.Equal(["warn: " + Strings.QuitEventTaken(name)], _log.Entries);
    }

    [Fact]
    public void An_event_made_first_with_access_the_tray_lacks_is_not_waited_on_either()
    {
        // As an unelevated tray sees the event of an elevated one that is still exiting.
        var name = UniqueName();
        var nobody = new EventWaitHandleSecurity();
        nobody.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        using var squatter = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out var created, nobody);
        Assert.True(created);

        Assert.Null(QuitEvent.Create(name, _log));
        Assert.Equal(["warn: " + Strings.QuitEventTaken(name)], _log.Entries);
    }

    [Fact]
    public void Once_disposed_the_tray_no_longer_listens()
    {
        var name = UniqueName();
        var quit = QuitEvent.Create(name, _log);
        Assert.NotNull(quit);
        var quits = 0;
        quit.OnSet(() => Interlocked.Increment(ref quits));
        ActAsInstaller(name);
        // Held open here, so the event outlives the tray's handle and can still be set.
        using var installer = EventWaitHandleAcl.OpenExisting(name, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize);

        quit.Dispose();
        installer.Set();

        // The event stays set: nobody waits on it any more.
        Assert.True(installer.WaitOne(Wait.Timeout));
        Assert.Equal(0, Volatile.Read(ref quits));
    }

    /// TrayStopper, as `marquee task stop` runs it, reaching a tray in this process: it names the event by the tray's
    /// session (Session\<id>\…), as it must from the installer's session 0, and the tray made it as Local\…; in the same
    /// session the two are one event.
    [Fact]
    public void The_installer_quits_the_tray_through_the_trays_session()
    {
        var name = UniqueName(out var id);
        using var quit = QuitEvent.Create(name, _log);
        Assert.NotNull(quit);
        using var exited = new ManualResetEventSlim();
        quit.OnSet(exited.Set);
        ActAsInstaller(name);
        using var tray = new InProcessTray($@"Session\{Process.GetCurrentProcess().SessionId}\{id}", exited);

        var result = TrayStopper.Stop(Tray, new Trays(tray), Wait.Timeout);

        Assert.Equal(new TrayStopResult(Quit: 1, Killed: 0), result);
        Assert.False(tray.Killed);
    }

    /// A tray that "exits" when this process hears its quit event, which TrayStopper's own code sets.
    sealed class InProcessTray(string quitEventName, ManualResetEventSlim exited) : ITray
    {
        public bool Killed { get; private set; }

        public int ProcessId => Environment.ProcessId;

        public bool AskToQuit() => TrayStopper.AskToQuit(quitEventName);

        public bool WaitForExit(TimeSpan timeout) => exited.Wait(timeout);

        public void Kill() => Killed = true;

        public void Dispose()
        {
        }
    }

    sealed class Trays(params ITray[] trays) : ITrayProcesses
    {
        public IReadOnlyList<ITray> Running(string trayExePath) => trays;
    }
}
