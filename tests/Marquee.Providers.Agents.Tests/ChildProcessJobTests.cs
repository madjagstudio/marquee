using System.Diagnostics;
using Marquee.Providers.Agents.Claude;

namespace Marquee.Providers.Agents.Tests;

public class ChildProcessJobTests
{
    // What happens to the shared job when Marquee exits or crashes: the kernel closes its handle. Closing a job of our own
    // here does the same, without touching the shared one other tests' children are in.
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

    [Fact]
    public void There_is_one_shared_job() => Assert.Same(ChildProcessJob.Shared, ChildProcessJob.Shared);
}
