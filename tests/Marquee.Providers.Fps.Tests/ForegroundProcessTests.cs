using System.Diagnostics;
using Marquee.Providers.Fps;

namespace Marquee.Providers.Fps.Tests;

public class ForegroundProcessTests
{
    [Fact]
    public void The_foreground_is_a_running_process_or_none()
    {
        // A CI service session may have no foreground window at all; a desktop always has one.
        if (ForegroundProcess.Id() is not { } id) return;

        using var process = Process.GetProcessById(id);
        Assert.False(process.HasExited);
    }

    [Fact]
    public void A_process_name_has_no_exe_on_the_end()
    {
        using var self = Process.GetCurrentProcess();

        Assert.Equal(self.ProcessName, ForegroundProcess.Name(Environment.ProcessId));
        Assert.DoesNotContain(".exe", ForegroundProcess.Name(Environment.ProcessId));
    }

    [Fact]
    public void A_process_that_is_gone_has_no_name()
    {
        Assert.Null(ForegroundProcess.Name(-1));
    }
}
