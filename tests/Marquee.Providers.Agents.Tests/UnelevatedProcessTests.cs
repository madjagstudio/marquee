using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Tests.TestSupport;
using Xunit.Abstractions;

namespace Marquee.Providers.Agents.Tests;

/// UnelevatedProcess's shell-token start (GetShellWindow … CreateProcessWithTokenW) needs an elevated Marquee, so it is
/// checked by hand. Everything around it (command line, environment block, pipes, job, suspended start, timeout) runs
/// here through RunWithOwnToken, which differs only in creating the child with CreateProcessW under the test's own token.
public class UnelevatedProcessTests(ITestOutputHelper output) : IDisposable
{
    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// A full path, as ClaudeCli.Find always gives: the program is not searched for on PATH.
    static readonly string PowerShellExe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    static string[] PowerShell(string script) => ["-NoProfile", "-NonInteractive", "-Command", script];

    ProcessResult Run(string script, TimeSpan? timeout = null) =>
        UnelevatedProcess.RunWithOwnToken(PowerShellExe, PowerShell(script), _dir.Path, timeout ?? TimeSpan.FromSeconds(30));

    // ---- Choosing it ----

    [Fact]
    public void The_elevation_check_agrees_with_the_administrators_role()
    {
        // Unelevated (the dev PC): both false. With UAC off (CI runners) an administrator's token is elevated: both true.
        using var identity = WindowsIdentity.GetCurrent();
        var administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

        Assert.Equal(administrator, ProcessElevation.IsCurrentProcessElevated());
    }

    [Fact]
    public void Claude_runs_through_UnelevatedProcess_only_when_Marquee_is_elevated()
    {
        Assert.Equal((ProcessRunner)UnelevatedProcess.Run, ClaudeCli.Runner(elevated: true));
        Assert.Equal((ProcessRunner)ClaudeCli.RunProcess, ClaudeCli.Runner(elevated: false));
    }

    [Fact]
    public void The_shell_token_is_the_users_own()
    {
        // Every step up to CreateProcessWithTokenW works unelevated. Without a desktop shell (a CI service session) it
        // must refuse, saying why.
        NativeMethods.KernelHandle token;
        try
        {
            token = UnelevatedProcess.ShellUserToken();
        }
        catch (InvalidOperationException ex) when (NativeMethods.GetShellWindow() == IntPtr.Zero)
        {
            Assert.Contains("no desktop shell", ex.Message);
            return;
        }

        using (token)
        {
            using var shellUser = new WindowsIdentity(token.DangerousGetHandle());
            using var self = WindowsIdentity.GetCurrent();
            Assert.Equal(self.User, shellUser.User);
            if (!ProcessElevation.IsCurrentProcessElevated()) Assert.False(ProcessElevation.IsElevated(token));
        }
    }

    [Theory]
    [InlineData(NativeMethods.TokenElevationTypeDefault, true)]  // UAC off (or a standard user): no boundary to protect
    [InlineData(NativeMethods.TokenElevationTypeLimited, true)]  // an administrator's filtered token
    [InlineData(NativeMethods.TokenElevationTypeFull, false)]    // Explorer itself is elevated
    [InlineData(0, false)]
    [InlineData(4, false)]
    public void Only_a_default_or_limited_shell_token_may_start_claude(int elevationType, bool allowed) =>
        Assert.Equal(allowed, UnelevatedProcess.MayRunClaudeAs(elevationType));

    [Fact]
    public void The_shell_tokens_elevation_type_is_readable_and_allowed_here()
    {
        // The dev PC's Explorer is unelevated (Limited, or Default with UAC off). A CI service session has no shell.
        NativeMethods.KernelHandle token;
        try
        {
            token = UnelevatedProcess.ShellUserToken();
        }
        catch (InvalidOperationException) when (NativeMethods.GetShellWindow() == IntPtr.Zero)
        {
            return;
        }

        using (token) Assert.True(UnelevatedProcess.MayRunClaudeAs(ProcessElevation.ElevationType(token)));
    }

    [Fact]
    public void Run_starts_the_child_with_the_shell_token_when_elevated_and_refuses_cleanly_otherwise()
    {
        // Unelevated, CreateProcessWithTokenW is refused (it needs SeImpersonatePrivilege), so this checks the refusal.
        // Run from an elevated terminal, it starts PowerShell through the real path and checks it isn't an administrator.
        const string script =
            "$env:DISABLE_AUTOUPDATER; ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')";
        var elevated = ProcessElevation.IsCurrentProcessElevated();
        try
        {
            var result = UnelevatedProcess.Run(PowerShellExe, PowerShell(script), _dir.Path, TimeSpan.FromSeconds(30));

            Assert.True(elevated, "an unelevated Marquee can't start a process with another token");
            bool shellElevated;
            using (var token = UnelevatedProcess.ShellUserToken()) shellElevated = ProcessElevation.IsElevated(token);
            var lines = result.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(["1", shellElevated ? "True" : "False"], lines); // False unless UAC is off
        }
        catch (InvalidOperationException ex)
        {
            output.WriteLine(ex.Message);
            Assert.StartsWith("Marquee is running elevated and couldn't start claude without elevation (", ex.Message);
            Assert.EndsWith("); Claude Code limits come only from the status line.", ex.Message);
            Assert.True(!elevated || NativeMethods.GetShellWindow() == IntPtr.Zero, ex.Message);
        }
    }

    // ---- Command line ----

    [Fact]
    public void The_usage_command_line_is_the_one_Process_would_build()
    {
        Assert.Equal(
            "\"C:\\Users\\a b\\.local\\bin\\claude.exe\" "
            + """-p /usage --no-session-persistence --setting-sources user --settings "{\"disableAllHooks\":true}" --strict-mcp-config --output-format stream-json --verbose""",
            UnelevatedProcess.CommandLine(@"C:\Users\a b\.local\bin\claude.exe", ClaudeCli.UsageArguments));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData("""{"disableAllHooks":true}""")]
    [InlineData(@"C:\Program Files\x\")]
    [InlineData(@"ends\\")]
    [InlineData(@"x\""y")]
    [InlineData(@"x\\""y")]
    [InlineData(@"\\server\share")]
    [InlineData("tab\there")]
    public void Each_argument_comes_back_whole_from_the_command_line(string argument)
    {
        var parsed = Parse(UnelevatedProcess.CommandLine(@"C:\a b\claude.exe", ["first", argument, "last"]));

        Assert.Equal([@"C:\a b\claude.exe", "first", argument, "last"], parsed);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern IntPtr LocalFree(IntPtr memory);

    /// How a C runtime program splits its command line.
    static string[] Parse(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero) throw new Win32Exception();
        try
        {
            return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!).ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    // ---- Environment block ----

    [Fact]
    public void The_environment_block_is_sorted_null_separated_and_has_the_settings()
    {
        var block = UnelevatedProcess.EnvironmentBlock(
            ["Path=C:\\Windows", "=C:=C:\\x", "disable_autoupdater=0", "TEMP=C:\\Temp", "no separator"],
            new Dictionary<string, string> { ["DISABLE_AUTOUPDATER"] = "1", ["NEW"] = "v" });

        Assert.Equal("=C:=C:\\x\0DISABLE_AUTOUPDATER=1\0NEW=v\0Path=C:\\Windows\0TEMP=C:\\Temp\0\0", block);
    }

    // ---- Running ----

    [Fact]
    public void It_captures_stdout_and_the_exit_code()
    {
        var result = Run("Write-Output 'hello from child'; exit 3");

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("hello from child", result.Output.Trim());
    }

    [Fact]
    public void It_runs_in_the_working_directory_and_passes_each_argument_whole()
    {
        var script = _dir.Write("args.ps1", "$args[0]; $args[1]; $args.Count; (Get-Location).Path");

        var result = UnelevatedProcess.RunWithOwnToken(PowerShellExe,
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "a b", "{\"c\":1}"], _dir.Path, TimeSpan.FromSeconds(30));

        var lines = result.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["a b", "{\"c\":1}", "2", _dir.Path], lines);
    }

    [Fact]
    public void It_closes_stdin_straight_away() =>
        Assert.Equal("0", Run("[Console]::In.ReadToEnd().Length").Output.Trim());

    [Fact]
    public void It_reads_stderr_so_its_pipe_cannot_fill()
    {
        var result = Run("[Console]::Error.Write(('e' * 400000)); Write-Output 'done'");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("done", result.Output.Trim());
    }

    [Fact]
    public void It_keeps_at_most_one_megabyte_of_stdout_and_still_lets_the_child_finish()
    {
        var result = Run("[Console]::Out.Write(('y' * 2500000))");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ClaudeCli.MaxOutputBytes, result.Output.Length);
    }

    [Fact]
    public void It_gives_the_child_the_users_environment_without_the_autoupdater()
    {
        var printed = Run("$env:DISABLE_AUTOUPDATER; $env:USERPROFILE").Output;

        var lines = printed.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["1", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)], lines);
    }

    [Fact]
    public async Task It_puts_the_child_in_the_job_that_dies_with_Marquee()
    {
        var pidFile = _dir.File("child.pid");
        var inJob = false;
        ChildProcessJob.Assigned.Value = (job, process) => inJob = job.Contains(process);
        var running = Task.Run(() => Run($"Set-Content -Path '{pidFile}' -Value $PID; Start-Sleep -Seconds 3"));

        ClaudeUsageTests.WaitForPid(pidFile);
        Assert.True(inJob, "the child is not in a job of its own");
        Assert.Equal(0, (await running).ExitCode);
    }

    [Fact]
    public void It_kills_a_hung_child_at_the_timeout_and_throws()
    {
        var pidFile = _dir.File("child.pid");

        var clock = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => Run($"Set-Content -Path '{pidFile}' -Value $PID; Start-Sleep -Seconds 120", TimeSpan.FromSeconds(8)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "it stopped at the timeout, not when the child finished");
        Assert.True(File.Exists(pidFile), "the child started and recorded its process id before the timeout (slow machine?)");
        var pid = int.Parse(File.ReadAllText(pidFile).Trim());
        Assert.True(Wait.Until(() => !IsRunning(pid)), $"process {pid} is still running");
    }

    [Fact]
    public void It_reports_a_program_that_cannot_start() =>
        Assert.Throws<Win32Exception>(() => UnelevatedProcess.RunWithOwnToken(_dir.File("missing.exe"), [], _dir.Path, TimeSpan.FromSeconds(5)));

    [Fact]
    public void It_refuses_a_program_without_a_full_path()
    {
        Assert.Throws<ArgumentException>(() => UnelevatedProcess.RunWithOwnToken("powershell.exe", [], _dir.Path, TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentException>(() => UnelevatedProcess.Run("claude.exe", [], _dir.Path, TimeSpan.FromSeconds(5)));
    }

    static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
