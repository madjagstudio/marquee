using System.Diagnostics;

namespace Marquee.App.Tests.TestSupport;

/// Junctions for the tests of code that must not follow them.
static class Links
{
    /// Junctions, unlike symbolic links, need no special rights; .NET has no API to make one. cmd.exe gets a stdin of
    /// its own, closed at once, so it never waits on the test runner's; one stalled by a virus scanner or a busy disk
    /// fails the test after 30 s rather than hanging the run.
    public static void MakeJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        _ = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"mklink /J {link} didn't finish within 30 s, so it was ended.");
        }

        Assert.True(process.ExitCode == 0 && Directory.Exists(link),
            $"mklink /J failed with exit code {process.ExitCode}: {(error.Wait(TimeSpan.FromSeconds(5)) ? error.Result.Trim() : "")}");
    }

    /// Removes a junction without touching what it points to: a recursive delete of the folder holding it would be
    /// refused without admin rights, and leave it behind.
    public static void RemoveJunction(string link)
    {
        if (Directory.Exists(link)) Directory.Delete(link);
    }
}
