using System.Security.AccessControl;
using Marquee.Host;

namespace Marquee.App.Tests;

/// TrayScreen is in Marquee.Host, but making a mutex with an access list takes System.Threading.AccessControl, which
/// comes with the Windows Desktop framework the tray uses, so this case is tested here.
public class TrayScreenAccessTests
{
    [Fact]
    public void A_marker_marquee_run_may_not_open_counts_as_held()
    {
        // As an unelevated `marquee run` sees the elevated tray's marker: there, but refused.
        var name = $@"Local\Marquee-Test-Screen-{Guid.NewGuid():N}";
        var nobody = new MutexSecurity();
        nobody.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        using var elevated = MutexAcl.Create(false, name, out _, nobody);

        Assert.True(TrayScreen.IsHeld(name));
    }
}
