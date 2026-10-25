using System.Diagnostics;
using Marquee.Core.Processes;

namespace Marquee.Core.Tests;

public class ChildProcessJobTests
{
    // What happens to a child's job when its run ends, or when Marquee exits or crashes and the kernel closes the handle.
    [Fact]
    public void Closing_the_job_kills_the_processes_in_it()
    {
        using var child = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 60" },
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        try
        {
            var job = new ChildProcessJob();
            job.Assign(child.SafeHandle);
            Assert.True(job.Contains(child.SafeHandle));
            Assert.False(child.HasExited);

            job.Dispose();

            Assert.True(child.WaitForExit(10_000), "the child outlived its job");
        }
        finally
        {
            if (!child.HasExited) child.Kill();
        }
    }

    [Fact]
    public void A_process_that_was_never_assigned_is_not_in_the_job()
    {
        using var job = new ChildProcessJob();
        using var self = Process.GetCurrentProcess();

        Assert.False(job.Contains(self.SafeHandle));
    }

    // Why each child gets a job of its own. An unelevated claude starts inside the Secondary Logon service's job; here a
    // process in an unrelated job stands in for it. Windows refuses to move it into a job that already holds another
    // process ("Access is denied", observed with one shared job), and accepts a new, empty job.
    [Fact]
    public void A_process_already_in_another_job_can_only_join_a_new_empty_job()
    {
        using var earlier = StartSleeper();
        using var child = StartSleeper();
        using var used = new ChildProcessJob();
        using var other = new ChildProcessJob();
        try
        {
            used.Assign(earlier.SafeHandle);
            other.Assign(child.SafeHandle);

            var refused = Assert.Throws<System.ComponentModel.Win32Exception>(() => used.Assign(child.SafeHandle));
            Assert.Equal(5, refused.NativeErrorCode); // ERROR_ACCESS_DENIED

            using var fresh = new ChildProcessJob();
            fresh.Assign(child.SafeHandle);
            Assert.True(fresh.Contains(child.SafeHandle));
        }
        finally
        {
            if (!earlier.HasExited) earlier.Kill();
            if (!child.HasExited) child.Kill();
        }
    }

    static Process StartSleeper() => Process.Start(new ProcessStartInfo("powershell.exe")
    {
        ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 60" },
        UseShellExecute = false,
        CreateNoWindow = true,
    })!;
}
