using System.Diagnostics;

namespace Marquee.Editor.Model.Tests.TestSupport;

/// Junctions and symbolic links, for the tests of code that must not follow them (as Marquee.Core.Tests makes them).
static class Links
{
    /// mklink takes milliseconds. A cmd.exe stalled by a virus scanner or a busy disk fails its test after this,
    /// rather than hanging the whole run.
    static readonly TimeSpan MklinkTimeout = TimeSpan.FromSeconds(30);

    /// Junctions, unlike symbolic links, need no special rights; .NET has no API to make one. cmd.exe gets a stdin of
    /// its own, closed at once, so it never waits on the test runner's, and its output is drained as it comes.
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
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(MklinkTimeout))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"mklink /J {link} didn't finish within {MklinkTimeout.TotalSeconds:0} s, so it was ended.");
        }

        Assert.True(process.ExitCode == 0 && Directory.Exists(link),
            $"mklink /J failed with exit code {process.ExitCode}:{Said(output)}{Said(error)}");
    }

    /// Removes a junction without touching what it points to. .NET's recursive delete removes a junction with
    /// DeleteVolumeMountPoint, which is refused without admin rights; removing it as a directory works for anyone.
    public static void RemoveJunction(string link)
    {
        if (Directory.Exists(link)) Directory.Delete(link);
    }

    /// A symbolic link to a file, or false where Windows won't make one: it does only for administrators or with
    /// developer mode on. The junction tests cover the same checks on any machine.
    public static bool TryMakeFileSymbolicLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// What a stream said, or nothing if it hasn't closed yet: a failure message mustn't wait on it.
    static string Said(Task<string> read) => read.Wait(TimeSpan.FromSeconds(5)) && read.Result.Trim() is { Length: > 0 } said ? " " + said : "";
}
