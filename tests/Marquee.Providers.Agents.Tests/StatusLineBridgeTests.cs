using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

public class StatusLineBridgeTests
{
    const string Input = """{"session_id":"x","rate_limits":{"five_hour":{"used_percentage":5}}}""";

    static (int Exit, string Output, string Errors, byte[]? Forwarded) Bridge(string? previous, Func<byte[], bool>? forward = null, string input = Input)
    {
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();
        byte[]? forwarded = null;
        var exit = StatusLineBridge.Run(new MemoryStream(Encoding.UTF8.GetBytes(input)), stdout, stderr, previous, ShellCommand.PowerShell,
            forward ?? (bytes =>
            {
                forwarded = bytes;
                return true;
            }));
        return (exit, Encoding.UTF8.GetString(stdout.ToArray()), stderr.ToString(), forwarded);
    }

    [Fact]
    public void Forwards_the_input_and_prints_the_previous_commands_output()
    {
        var (exit, output, errors, forwarded) = Bridge("$j = [Console]::In.ReadToEnd() | ConvertFrom-Json; Write-Output \"[$($j.session_id)] ok\"");

        Assert.Equal(0, exit);
        Assert.Equal("[x] ok\r\n", output);
        Assert.Equal("", errors);
        Assert.Equal(Input, Encoding.UTF8.GetString(forwarded!));
    }

    [Fact]
    public void Without_a_previous_command_it_prints_nothing()
    {
        var (exit, output, _, forwarded) = Bridge(null);

        Assert.Equal(0, exit);
        Assert.Equal("", output);
        Assert.NotNull(forwarded);
    }

    [Fact]
    public void Output_is_passed_through_byte_for_byte()
    {
        // Windows PowerShell 5.1 has no `e escape, so ESC is built from its code.
        var (_, output, _, _) = Bridge("$e = [char]27; [Console]::Out.Write(\"$e[32mgreen$e[0m line1`nline2\")");

        Assert.Equal("\u001b[32mgreen\u001b[0m line1\nline2", output);
    }

    [Fact]
    public void Marquee_not_running_changes_nothing()
    {
        var (exit, output, errors, _) = Bridge("Write-Output hi", _ => false);

        Assert.Equal((0, "hi\r\n", ""), (exit, output, errors));
    }

    [Fact]
    public void A_failing_forward_is_reported_on_stderr_only()
    {
        var (exit, output, errors, _) = Bridge("Write-Output hi", _ => throw new IOException("pipe broke"));

        Assert.Equal((0, "hi\r\n"), (exit, output));
        Assert.Contains("could not reach Marquee: pipe broke", errors);
    }

    [Fact]
    public void The_previous_commands_exit_code_is_kept()
    {
        var (exit, output, _, _) = Bridge("Write-Output partial; exit 3");

        Assert.Equal((3, "partial\r\n"), (exit, output));
    }

    [Fact]
    public void A_shell_that_cannot_start_is_reported()
    {
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();

        var exit = StatusLineBridge.Run(new MemoryStream(Encoding.UTF8.GetBytes(Input)), stdout, stderr, "echo hi",
            new ShellCommand(@"C:\nope\bash.exe", ["-c"]), _ => true);

        Assert.Equal(1, exit);
        Assert.Contains(@"could not run the previous status-line command with C:\nope\bash.exe", stderr.ToString());
    }

    [Fact]
    public void A_command_that_never_reads_its_input_still_finishes()
    {
        var (exit, output, _, _) = Bridge("Write-Output done", input: new string('x', 200_000));

        Assert.Equal((0, "done\r\n"), (exit, output));
    }

    [Fact]
    public void A_hung_command_is_stopped_at_the_timeout()
    {
        var watch = Stopwatch.StartNew();
        var result = ShellCommand.PowerShell.Run("Start-Sleep -Seconds 30", [], TimeSpan.FromMilliseconds(1500));

        Assert.True(result.TimedOut);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task A_background_child_holding_output_does_not_hold_up_the_result()
    {
        using var dir = new TempDir();
        var pidFile = dir.File("child.pid");
        // The child inherits stdout, so the pipe stays open after the shell exits; its pid goes to a file so the test can end it.
        var command = $"$p = Start-Process -NoNewWindow -PassThru powershell -ArgumentList '-NoProfile','-Command','Start-Sleep 30'; " +
                      $"Set-Content -Path '{pidFile}' -Value $p.Id; Write-Output done";
        var watch = Stopwatch.StartNew();
        var run = Task.Run(() => ShellCommand.PowerShell.Run(command, [], TimeSpan.FromSeconds(3)));
        try
        {
            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.Same(run, finished);

            var result = await run;
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
            Assert.False(result.TimedOut);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("done", Encoding.UTF8.GetString(result.Output));
        }
        finally
        {
            KillChild(pidFile);
        }
    }

    static void KillChild(string pidFile)
    {
        try
        {
            if (!File.Exists(pidFile) || !int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid)) return;
            using var child = Process.GetProcessById(pid);
            child.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    [Fact]
    public void Saved_status_line_round_trips_its_command()
    {
        using var dir = new TempDir();
        var path = dir.Write("claude-statusline.json", """{ "statusLine": { "type": "command", "command": "bash ~/sl.sh", "padding": 1 } }""");

        Assert.True(SavedStatusLine.TryLoad(path, out var statusLine));
        Assert.Equal("bash ~/sl.sh", SavedStatusLine.Command(statusLine));
        Assert.Equal(1, (int)statusLine!["padding"]!);
    }

    [Theory]
    [InlineData("""{ "statusLine": null }""")]
    [InlineData("""{ "statusLine": { "type": "static", "command": "x" } }""")]
    [InlineData("""{ "statusLine": { "type": "command", "command": "" } }""")]
    [InlineData("""{ "statusLine": { "type": "command", "command": 5 } }""")]
    [InlineData("not json")]
    public void Saved_status_lines_without_a_usable_command(string json)
    {
        using var dir = new TempDir();

        Assert.True(SavedStatusLine.TryLoad(dir.Write("saved.json", json), out var statusLine));
        Assert.Null(SavedStatusLine.Command(statusLine));
    }

    [Fact]
    public void No_saved_file_means_setup_never_ran()
    {
        using var dir = new TempDir();

        Assert.False(SavedStatusLine.TryLoad(dir.File("missing.json"), out _));
    }

    [Fact]
    public void Command_without_a_type_is_a_command() =>
        Assert.Equal("x", SavedStatusLine.Command(new JsonObject { ["command"] = "x" }));
}
