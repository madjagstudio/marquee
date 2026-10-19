using System.Diagnostics;
using Marquee.Providers.Fps;

namespace Marquee.Providers.Fps.Tests;

/// The real Windows calls behind PresentMonSession. FpsProviderTests covers what it does with them, through fakes.
/// Listing sessions works unelevated (it lists the ones the user may see), and stopping one that doesn't exist reports
/// that it isn't there, so these need no elevation and change nothing.
public class PresentMonSessionTests
{
    [Fact]
    public void The_machines_sessions_can_be_listed()
    {
        var names = EtwSessions.Instance.Names();

        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void Stopping_a_session_that_isnt_running_is_fine()
    {
        EtwSessions.Instance.Stop("Marquee-test-" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void This_process_is_running()
    {
        Assert.True(PresentMonSession.IsRunning(Environment.ProcessId));
    }

    [Fact]
    public void A_process_that_has_exited_is_not_running()
    {
        var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
        var id = process.Id;
        Assert.True(process.WaitForExit(30_000));
        process.Dispose(); // the last handle to it, so Windows forgets it

        Assert.False(PresentMonSession.IsRunning(id));
        Assert.False(PresentMonSession.IsRunning(-1));
    }
}
