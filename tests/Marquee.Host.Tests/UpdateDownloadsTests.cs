using System.Diagnostics;
using Marquee.Core.Files;
using Marquee.Host.Tests.TestSupport;
using Marquee.Host.Updates;

namespace Marquee.Host.Tests;

/// What the uninstaller deletes from %ProgramData%\Marquee\updates, in a temp folder standing in for it.
public class UpdateDownloadsTests
{
    [Fact]
    public void Removing_deletes_the_downloaded_installers_and_then_the_folder()
    {
        using var temp = new TempDir();
        var updates = temp.Folder(@"Marquee\updates");
        File.WriteAllText(Path.Combine(updates, "Marquee-0.2.0-x64.msi"), "msi");
        File.WriteAllText(Path.Combine(updates, "Marquee-0.2.0-x64.msi.sig"), "sig");
        File.WriteAllText(Path.Combine(updates, "Marquee-0.2.0-x64.msi.0123.partial"), "part");
        File.WriteAllText(Path.Combine(updates, "Marquee-0.2.0-x64.msi.log"), "msiexec's log");

        Assert.Equal(4, UpdateDownloads.Remove(updates));

        Assert.False(Directory.Exists(updates));
    }

    [Fact]
    public void Removing_keeps_files_Marquee_did_not_download_and_so_the_folder()
    {
        using var temp = new TempDir();
        var updates = temp.Folder(@"Marquee\updates");
        File.WriteAllText(Path.Combine(updates, "Marquee-0.2.0-x64.msi"), "msi");
        File.WriteAllText(Path.Combine(updates, "notes.txt"), "mine");

        Assert.Equal(1, UpdateDownloads.Remove(updates));

        Assert.Equal(["notes.txt"], Directory.GetFiles(updates).Select(Path.GetFileName));
    }

    [Fact]
    public void A_folder_that_is_not_there_is_nothing_to_remove()
    {
        using var temp = new TempDir();

        Assert.Equal(0, UpdateDownloads.Remove(temp.File(@"Marquee\updates")));
    }

    /// The uninstaller runs as SYSTEM: a junction in place of the folder, or of the folder above it, could otherwise
    /// point its deletes anywhere.
    [Theory]
    [InlineData(@"Marquee\updates", @"Marquee\updates")]
    [InlineData("Marquee", @"Marquee\updates")]
    public void A_junction_is_refused_and_what_it_points_to_is_kept(string link, string updates)
    {
        using var temp = new TempDir();
        var target = temp.Folder(@"elsewhere\updates");
        var planted = temp.Write(@"elsewhere\updates\Marquee-0.2.0-x64.msi", "someone else's");
        Directory.CreateDirectory(Path.GetDirectoryName(temp.File(link))!);
        MakeJunction(temp.File(link), link == "Marquee" ? Path.GetDirectoryName(target)! : target);
        try
        {
            Assert.Throws<UnsafePathException>(() => UpdateDownloads.Remove(temp.File(updates)));

            Assert.True(File.Exists(planted));
        }
        finally
        {
            Directory.Delete(temp.File(link));
        }
    }

    /// Junctions need no special rights, unlike symbolic links; .NET has no API to make one. cmd.exe gets a stdin of its
    /// own, closed at once, so it never waits on the test runner's; one stalled by a virus scanner or a busy disk fails
    /// the test after 30 s rather than hanging the run.
    static void MakeJunction(string link, string target)
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
}
