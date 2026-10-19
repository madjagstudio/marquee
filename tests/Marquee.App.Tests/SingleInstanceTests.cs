using System.Security.AccessControl;
using System.Security.Principal;
using Marquee.App.Tests.TestSupport;

namespace Marquee.App.Tests;

/// The named objects that keep one tray per user. Each test makes names of its own, so a real tray running on this PC
/// and the other tests are never involved.
public class SingleInstanceTests
{
    static (string Instance, string Show) UniqueNames()
    {
        var id = Guid.NewGuid().ToString("N");
        return ($@"Local\Marquee-Test-Tray-{id}", $@"Local\Marquee-Test-Tray-Show-{id}");
    }

    /// A mutex nobody may open, as an unelevated start sees the elevated tray's: there, but refused.
    static Mutex Refused(string name)
    {
        var nobody = new MutexSecurity();
        nobody.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var mutex = MutexAcl.Create(false, name, out var created, nobody);
        Assert.True(created);
        return mutex;
    }

    [Fact]
    public void The_first_claim_wins_until_it_is_let_go()
    {
        var (instance, show) = UniqueNames();

        using (var first = SingleInstance.TryClaim(instance, show))
        {
            Assert.NotNull(first);
            Assert.Null(SingleInstance.TryClaim(instance, show));
        }

        using var again = SingleInstance.TryClaim(instance, show);
        Assert.NotNull(again);
    }

    [Fact]
    public void A_second_start_asks_the_first_to_open_its_menu()
    {
        var (instance, show) = UniqueNames();
        using var first = SingleInstance.TryClaim(instance, show)!;
        using var asked = new SemaphoreSlim(0);
        first.OnShowRequested(() => asked.Release());

        Assert.True(SingleInstance.AskFirstToShow(show));
        Assert.True(asked.Wait(Wait.Timeout));

        // It keeps listening.
        Assert.True(SingleInstance.AskFirstToShow(show));
        Assert.True(asked.Wait(Wait.Timeout));
    }

    [Fact]
    public void With_no_first_instance_there_is_nobody_to_ask()
    {
        Assert.False(SingleInstance.AskFirstToShow(UniqueNames().Show));
    }

    [Fact]
    public void The_user_may_signal_the_tray_even_when_it_runs_elevated()
    {
        // What an elevated process makes gets the elevated token's default access (administrators only) unless it says
        // otherwise, so the show event says that the user may set it: a start from the Start menu is unelevated.
        var (instance, show) = UniqueNames();
        using var first = SingleInstance.TryClaim(instance, show)!;
        using var current = WindowsIdentity.GetCurrent();
        using var opened = EventWaitHandleAcl.OpenExisting(show, EventWaitHandleRights.ReadPermissions);

        var rules = opened.GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<EventWaitHandleAccessRule>();

        var user = Assert.Single(rules, rule => rule.IdentityReference == current.User);
        Assert.Equal(AccessControlType.Allow, user.AccessControlType);
        Assert.Equal(EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, user.EventWaitHandleRights);
    }

    [Fact]
    public void A_tray_this_process_may_not_open_still_counts_as_running()
    {
        var (instance, show) = UniqueNames();
        using var elevated = Refused(instance);

        Assert.Null(SingleInstance.TryClaim(instance, show));
    }
}
