using System.Security.Principal;
using Marquee.Core.Processes;

namespace Marquee.Core.Tests;

public class ProcessElevationTests
{
    [Fact]
    public void The_elevation_check_agrees_with_the_administrators_role()
    {
        // Unelevated (the dev PC): both false. With UAC off (CI runners) an administrator's token is elevated: both true.
        using var identity = WindowsIdentity.GetCurrent();
        var administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

        Assert.Equal(administrator, ProcessElevation.IsCurrentProcessElevated());
    }
}
