using System.Diagnostics;
using System.Security.Cryptography;
using Marquee.Core.Processes;
using Marquee.Providers.Fps;
using Marquee.Providers.Fps.Tests.TestSupport;

namespace Marquee.Providers.Fps.Tests;

/// The real child process behind FpsProvider, with PowerShell standing in for PresentMon (which needs admin rights):
/// the same pipes, job and exit handling, and the same reading of its CSV.
public class PresentMonChildTests
{
    static readonly string PowerShellExe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    static string[] PowerShell(string script) => ["-NoProfile", "-NonInteractive", "-Command", script];

    [Fact]
    public void Its_csv_reaches_the_frame_stats()
    {
        const string script =
            "'Application,ProcessID,SwapChainAddress,CPUStartTime,MsBetweenPresents'; " +
            "foreach ($i in 1..30) { 'game.exe,4242,0x10,' + ($i * 20) + ',20' }; Start-Sleep -Seconds 60";
        var clock = TimeSpan.FromSeconds(1);
        using var presentMon = new PresentMonProcess(PresentMonChild.Start(PowerShellExe, PowerShell(script)), () => clock);

        Assert.True(Wait.Until(() => presentMon.Stats.TryRead(4242, out var r) && r.Fps > 49.99), "The frames never arrived.");
        Assert.True(presentMon.Stats.TryRead(4242, out var reading));
        Assert.Equal(50, reading.Fps, 1e-6);
        Assert.Equal(clock, reading.LastArrival);
        Assert.Null(presentMon.Failure);
    }

    [Fact]
    public void Its_exit_code_and_last_error_line_say_why_it_stopped()
    {
        const string script = "[Console]::Error.WriteLine('first'); [Console]::Error.WriteLine('Error: no session'); exit 3";
        using var presentMon = new PresentMonProcess(PresentMonChild.Start(PowerShellExe, PowerShell(script)), () => TimeSpan.Zero);

        Assert.True(Wait.Until(() => presentMon.Failure is not null), "It never stopped.");

        Assert.Equal("PresentMon exited with code 3 (its last error: Error: no session).", presentMon.Failure);
    }

    [Fact]
    public void It_runs_in_a_kill_on_close_job_and_dispose_stops_it()
    {
        var inJob = false;
        ChildProcessJob.Assigned.Value = (job, process) => inJob = job.Contains(process);
        int id;
        using (var child = PresentMonChild.Start(PowerShellExe, PowerShell("Start-Sleep -Seconds 60")))
        {
            id = ((PresentMonChild)child).ProcessId;
            Assert.True(inJob);
            Assert.Null(child.ExitCode);
        }

        Assert.True(Wait.Until(() => !Running(id)), "The child outlived Dispose.");
    }

    [Fact]
    public void A_verified_exe_starts_while_it_is_held_open()
    {
        // FpsProvider keeps the checked file open, readable but not writable, until the child has started.
        using var dir = new TempDir();
        var exe = dir.File("PresentMon-test.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), exe);
        var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe)));

        IPresentMonRun run;
        using (PresentMonBundle.OpenVerified(exe, sha256))
            run = PresentMonChild.Start(exe, ["/c", "echo Application,ProcessID"]);

        using (run)
        {
            Assert.Equal("Application,ProcessID", run.Output.ReadLine());
            Assert.True(Wait.Until(() => run.ExitCode is not null), "It never exited.");
            Assert.Equal(0, run.ExitCode);
        }
    }

    static bool Running(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
