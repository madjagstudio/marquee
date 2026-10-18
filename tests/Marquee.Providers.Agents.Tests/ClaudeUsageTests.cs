using System.Diagnostics;
using System.Text.Json;
using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

public class ClaudeUsageTests : IDisposable
{
    // From the subscriber fixture: the five-hour window resets 2026-10-17T17:29:59.514759Z, the weekly one 2026-10-24T10:00:00Z.
    const double FiveHourReset = 1792258199.514;
    const double SevenDayReset = 1792836000;

    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    static string Usage(string name) => Fixture.Text($"usage/{name}");

    // ---- Parser ----

    [Theory]
    [InlineData("subscriber.jsonl")]
    [InlineData("api-key.jsonl")]
    [InlineData("no-fresh-limits.jsonl")]
    [InlineData("not-logged-in.jsonl")]
    public void Every_line_of_each_fixture_is_a_json_object(string name)
    {
        // A line the parser skips as unreadable would make a fixture test pass for the wrong reason.
        foreach (var line in Usage(name).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }
    }

    [Fact]
    public void A_subscriber_report_has_both_windows_and_no_context()
    {
        var status = ClaudeUsageReport.Parse(Usage("subscriber.jsonl"));

        Assert.Equal(12, status.FiveHour!.UsedPct);
        Assert.Equal(FiveHourReset, status.FiveHour.ResetsAt!.Value, 3);
        Assert.Equal(34.5, status.SevenDay!.UsedPct);
        Assert.Equal(SevenDayReset, status.SevenDay.ResetsAt);
        Assert.Null(status.ContextPct);
    }

    [Fact]
    public void An_api_key_report_has_no_windows_and_does_not_throw() =>
        Assert.Equal(new ClaudeStatus(null, null, null), ClaudeUsageReport.Parse(Usage("api-key.jsonl")));

    [Fact]
    public void A_report_with_null_limits_has_no_windows_and_does_not_throw() =>
        Assert.Equal(new ClaudeStatus(null, null, null), ClaudeUsageReport.Parse(Usage("no-fresh-limits.jsonl")));

    [Fact]
    public void Not_logged_in_throws_with_the_text_of_the_result()
    {
        var ex = Assert.Throws<FormatException>(() => ClaudeUsageReport.Parse(Usage("not-logged-in.jsonl")));

        Assert.Contains("usage_report", ex.Message);
        Assert.Contains("Not logged in · Please run /login", ex.Message);
    }

    [Fact]
    public void The_result_text_in_the_message_is_cut_at_120_characters()
    {
        var text = new string('x', 300);

        var ex = Assert.Throws<FormatException>(() => ClaudeUsageReport.Parse($$"""{"type":"result","result":"{{text}}"}"""));

        Assert.Contains(new string('x', 120), ex.Message);
        Assert.DoesNotContain(new string('x', 121), ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("not json at all")]
    [InlineData("""{"type":"system","subtype":"init"}""")]
    [InlineData("""{"type":"assistant","message":{"usage_report":null}}""")]
    public void Output_without_a_usage_report_is_a_format_error(string output) =>
        Assert.Throws<FormatException>(() => ClaudeUsageReport.Parse(output));

    [Theory]
    [InlineData("""{"type":"assistant","message":{"usage_report":"nope"}}""")]
    [InlineData("""{"type":"assistant","message":{"usage_report":[]}}""")]
    [InlineData("""{"type":"assistant","message":{"usage_report":{"rate_limits":"nope"}}}""")]
    [InlineData("""{"type":"assistant","message":{"usage_report":{"rate_limits":[]}}}""")]
    [InlineData("""{"type":"assistant","message":{"usage_report":{"rate_limits":7}}}""")]
    [InlineData("""{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":{}}}}}""")]
    [InlineData("""{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[{"kind":"session"}]}}}}""")]
    [InlineData("""{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[{"kind":"session","percent":"12"}]}}}}""")]
    [InlineData("""{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[{"kind":"weekly_all","percent":null}]}}}}""")]
    public void Malformed_shapes_are_format_errors(string output) =>
        Assert.Throws<FormatException>(() => ClaudeUsageReport.Parse(output));

    [Fact]
    public void A_missing_percent_names_the_kind()
    {
        var ex = Assert.Throws<FormatException>(() => ClaudeUsageReport.Parse(
            """{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[{"kind":"weekly_all"}]}}}}"""));

        Assert.Contains("weekly_all", ex.Message);
        Assert.Contains("percent", ex.Message);
    }

    [Fact]
    public void Unknown_kinds_and_entries_that_are_not_objects_are_ignored()
    {
        var status = ClaudeUsageReport.Parse(
            """{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[{"kind":"weekly_scoped","percent":"garbage"},{"kind":"something_new"},"text",null,{"kind":"weekly_all","percent":9,"resets_at":"2026-10-24T10:00:00Z"}]}}}}""");

        Assert.Null(status.FiveHour);
        Assert.Equal(new ClaudeWindow(9, SevenDayReset), status.SevenDay);
    }

    [Fact]
    public void An_empty_or_missing_limits_list_means_no_windows()
    {
        var empty = ClaudeUsageReport.Parse("""{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[]}}}}""");
        var missing = ClaudeUsageReport.Parse("""{"type":"assistant","message":{"usage_report":{"rate_limits":{}}}}""");

        Assert.Equal(new ClaudeStatus(null, null, null), empty);
        Assert.Equal(new ClaudeStatus(null, null, null), missing);
    }

    [Fact]
    public void Lines_that_do_not_parse_are_skipped()
    {
        var output = "garbage\n{\"type\":\n[1,2]\n42\n\"text\"\n\n"
            + """{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[{"kind":"session","percent":3,"resets_at":1792258200}]}}}}"""
            + "\r\n{truncated";

        Assert.Equal(new ClaudeWindow(3, 1792258200), ClaudeUsageReport.Parse(output).FiveHour);
    }

    [Fact]
    public void The_usage_report_may_be_at_the_top_level_of_the_line()
    {
        var status = ClaudeUsageReport.Parse(
            """{"type":"assistant","usage_report":{"rate_limits":{"limits":[{"kind":"session","percent":3,"resets_at":1792258200}]}}}""");

        Assert.Equal(new ClaudeWindow(3, 1792258200), status.FiveHour);
    }

    [Fact]
    public void A_reset_time_that_is_not_a_real_time_reads_as_unknown()
    {
        var status = ClaudeUsageReport.Parse(
            """{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[{"kind":"session","percent":3,"resets_at":"soon"},{"kind":"weekly_all","percent":4}]}}}}""");

        Assert.Equal(new ClaudeWindow(3, null), status.FiveHour);
        Assert.Equal(new ClaudeWindow(4, null), status.SevenDay);
    }

    // ---- Provider ----

    sealed class Harness
    {
        public DateTimeOffset Now = Clock.T0;
        public int Fetches;
        public Func<string> Fetch = () => throw new InvalidOperationException("no fetch set");
        public SensorStore Store { get; }
        public ClaudeUsageProvider Provider { get; }

        public Harness()
        {
            Store = new SensorStore(() => Now);
            Provider = new ClaudeUsageProvider(() =>
            {
                Fetches++;
                return Fetch();
            }, () => Now);
        }
    }

    [Fact]
    public void The_provider_is_named_and_polls_every_five_minutes()
    {
        using var provider = new ClaudeUsageProvider(() => "", () => Clock.T0);

        Assert.Equal("Claude Code limits (/usage)", provider.Name);
        Assert.Equal(TimeSpan.FromMinutes(5), provider.Interval);
        Assert.Equal(ClaudeUsageProvider.DefaultInterval, provider.Interval);
    }

    [Fact]
    public void Poll_writes_the_keys_and_they_expire_at_their_reset()
    {
        var h = new Harness { Fetch = () => Usage("subscriber.jsonl") };

        h.Provider.Poll(h.Store);

        Assert.Equal(12.0, Clock.Read(h.Store, "claude.five_hour.pct"));
        Assert.Equal(FiveHourReset, (double)Clock.Read(h.Store, "claude.five_hour.resets_at")!, 3);
        Assert.Equal(34.5, Clock.Read(h.Store, "claude.seven_day.pct"));
        Assert.Equal(SevenDayReset, Clock.Read(h.Store, "claude.seven_day.resets_at"));
        Assert.Equal(Clock.T0Unix, Clock.Read(h.Store, "claude.updated_at"));
        Assert.Null(Clock.Read(h.Store, "claude.context.pct"));

        h.Now = Clock.T0.AddHours(6); // past the five-hour reset, before the weekly one
        Assert.Null(Clock.Read(h.Store, "claude.five_hour.pct"));
        Assert.Null(Clock.Read(h.Store, "claude.five_hour.resets_at"));
        Assert.Equal(34.5, Clock.Read(h.Store, "claude.seven_day.pct"));
    }

    [Fact]
    public void A_poll_with_no_windows_writes_nothing_not_even_updated_at()
    {
        var h = new Harness { Fetch = () => Usage("no-fresh-limits.jsonl") };

        h.Provider.Poll(h.Store);

        Assert.Equal(1, h.Fetches);
        Assert.Null(Clock.Read(h.Store, "claude.updated_at"));
        Assert.Empty(h.Store.Snapshot().Keys);
    }

    [Fact]
    public void A_poll_with_no_windows_keeps_the_limits_already_in_the_store()
    {
        var h = new Harness { Fetch = () => Usage("subscriber.jsonl") };
        h.Provider.Poll(h.Store);

        h.Now = Clock.T0 + ClaudeUsageProvider.DefaultInterval;
        h.Fetch = () => Usage("no-fresh-limits.jsonl");
        h.Provider.Poll(h.Store);

        Assert.Equal(2, h.Fetches);
        Assert.Equal(12.0, Clock.Read(h.Store, "claude.five_hour.pct"));
        Assert.Equal(34.5, Clock.Read(h.Store, "claude.seven_day.pct"));
        Assert.Equal(Clock.T0Unix, Clock.Read(h.Store, "claude.updated_at")); // still the earlier reading's time
    }

    [Fact]
    public void A_failure_followed_by_an_immediate_poll_rethrows_without_fetching_again()
    {
        var h = new Harness { Fetch = () => throw new InvalidOperationException("claude exited with code 1") };

        var first = Assert.Throws<InvalidOperationException>(() => h.Provider.Poll(h.Store));
        h.Now = Clock.T0.AddSeconds(2);
        var second = Assert.Throws<InvalidOperationException>(() => h.Provider.Poll(h.Store));

        Assert.Equal(1, h.Fetches);
        Assert.Same(first, second);
        h.Now = Clock.T0.AddSeconds(ClaudeUsageProvider.DefaultInterval.TotalSeconds - 1);
        Assert.Throws<InvalidOperationException>(() => h.Provider.Poll(h.Store));
        Assert.Equal(1, h.Fetches);
    }

    [Fact]
    public void After_the_interval_the_provider_fetches_again()
    {
        var h = new Harness { Fetch = () => throw new InvalidOperationException("down") };
        Assert.Throws<InvalidOperationException>(() => h.Provider.Poll(h.Store));

        h.Now = Clock.T0 + ClaudeUsageProvider.DefaultInterval;
        h.Fetch = () => throw new TimeoutException("slow");
        Assert.Throws<TimeoutException>(() => h.Provider.Poll(h.Store));

        Assert.Equal(2, h.Fetches);
        // The cooldown restarts from that second attempt.
        h.Now += TimeSpan.FromSeconds(30);
        Assert.Throws<TimeoutException>(() => h.Provider.Poll(h.Store));
        Assert.Equal(2, h.Fetches);
    }

    [Fact]
    public void A_report_that_cannot_be_read_counts_as_a_failure()
    {
        var h = new Harness { Fetch = () => Usage("not-logged-in.jsonl") };

        var ex = Assert.Throws<FormatException>(() => h.Provider.Poll(h.Store));
        h.Now = Clock.T0.AddSeconds(1);
        Assert.Throws<FormatException>(() => h.Provider.Poll(h.Store));

        Assert.Contains("Not logged in", ex.Message);
        Assert.Equal(1, h.Fetches);
        Assert.Null(Clock.Read(h.Store, "claude.updated_at"));
    }

    [Fact]
    public void A_success_clears_the_cooldown()
    {
        var h = new Harness { Fetch = () => throw new InvalidOperationException("down") };
        Assert.Throws<InvalidOperationException>(() => h.Provider.Poll(h.Store));

        h.Now = Clock.T0 + ClaudeUsageProvider.DefaultInterval;
        h.Fetch = () => Usage("subscriber.jsonl");
        h.Provider.Poll(h.Store);

        // With the cooldown cleared, the very next poll runs instead of waiting out another interval.
        h.Now += TimeSpan.FromSeconds(1);
        h.Fetch = () => Usage("api-key.jsonl");
        h.Provider.Poll(h.Store);

        Assert.Equal(3, h.Fetches);
    }

    // ---- ClaudeCli.Find ----

    sealed class FakePc
    {
        public Dictionary<string, string> Environment { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = @"C:\Users\test",
            ["APPDATA"] = @"C:\Users\test\AppData\Roaming",
        };

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string[]> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? Find() => ClaudeCli.Find(
            name => Environment.TryGetValue(name, out var value) ? value : null,
            Files.Contains,
            path => Folders.TryGetValue(path, out var children) ? children : []);

        /// Adds %APPDATA%\Claude\claude-code\<version>\<hash>\claude.exe for each version (a "!" prefix leaves the exe out).
        public void DesktopApp(params string[] versions)
        {
            var root = @"C:\Users\test\AppData\Roaming\Claude\claude-code";
            Folders[root] = versions.Select(v => $@"{root}\{v.TrimStart('!')}").ToArray();
            foreach (var version in versions)
            {
                var folder = $@"{root}\{version.TrimStart('!')}";
                var hash = $@"{folder}\f0584be0fbaf";
                Folders[folder] = [hash];
                if (!version.StartsWith('!')) Files.Add($@"{hash}\claude.exe");
            }
        }
    }

    [Fact]
    public void Nothing_found_is_null() => Assert.Null(new FakePc().Find());

    [Fact]
    public void Find_looks_in_each_PATH_folder()
    {
        var pc = new FakePc();
        pc.Environment["PATH"] = @"C:\Windows;;C:\Tools\bin;C:\Other";
        pc.Files.Add(@"C:\Tools\bin\claude.exe");
        pc.Files.Add(@"C:\Other\claude.exe");

        Assert.Equal(@"C:\Tools\bin\claude.exe", pc.Find());
    }

    [Fact]
    public void A_PATH_folder_beats_the_native_installer_and_the_desktop_app()
    {
        var pc = new FakePc();
        pc.Environment["PATH"] = @"C:\Tools\bin";
        pc.Files.Add(@"C:\Tools\bin\claude.exe");
        pc.Files.Add(@"C:\Users\test\.local\bin\claude.exe");
        pc.DesktopApp("2.1.299");

        Assert.Equal(@"C:\Tools\bin\claude.exe", pc.Find());
    }

    [Fact]
    public void The_native_installer_location_beats_the_desktop_app()
    {
        var pc = new FakePc();
        pc.Environment["PATH"] = @"C:\Windows";
        pc.Files.Add(@"C:\Users\test\.local\bin\claude.exe");
        pc.DesktopApp("2.1.299");

        Assert.Equal(@"C:\Users\test\.local\bin\claude.exe", pc.Find());
    }

    [Fact]
    public void The_desktop_app_copy_is_used_last_and_the_newest_version_wins()
    {
        var pc = new FakePc();
        pc.DesktopApp("2.1.31", "2.1.299", "2.1.300", "2.0.999");

        Assert.Equal(@"C:\Users\test\AppData\Roaming\Claude\claude-code\2.1.300\f0584be0fbaf\claude.exe", pc.Find());
    }

    [Fact]
    public void Versions_compare_as_numbers_not_text()
    {
        var pc = new FakePc();
        pc.DesktopApp("2.1.31", "2.1.299");

        Assert.EndsWith(@"\2.1.299\f0584be0fbaf\claude.exe", pc.Find());
    }

    [Fact]
    public void Folders_that_are_not_versions_are_skipped()
    {
        var pc = new FakePc();
        pc.DesktopApp("latest", "2.1.31", "cache", "v9");

        Assert.EndsWith(@"\2.1.31\f0584be0fbaf\claude.exe", pc.Find());
    }

    [Fact]
    public void A_newer_version_folder_without_an_exe_falls_back_to_the_next()
    {
        var pc = new FakePc();
        pc.DesktopApp("!2.1.300", "2.1.299");

        Assert.EndsWith(@"\2.1.299\f0584be0fbaf\claude.exe", pc.Find());
    }

    [Fact]
    public void Missing_environment_variables_just_skip_those_locations()
    {
        var pc = new FakePc();
        pc.Environment.Clear();

        Assert.Null(pc.Find());
    }

    // ---- ClaudeCli.RunProcess / RunUsage ----

    static readonly string[] NoProfile = ["-NoProfile", "-NonInteractive", "-Command"];

    static string[] PowerShell(string script) => [.. NoProfile, script];

    [Fact]
    public void RunProcess_captures_stdout_and_the_exit_code()
    {
        var result = ClaudeCli.RunProcess("powershell.exe", PowerShell("Write-Output 'hello from child'; exit 0"), _dir.Path, TimeSpan.FromSeconds(30));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello from child", result.Output.Trim());
    }

    [Fact]
    public void RunProcess_runs_in_the_working_directory_and_passes_each_argument_whole()
    {
        // Arguments with spaces and quotes reach the child as they were given: no shell parses them.
        var script = _dir.Write("args.ps1", "$args[0]; $args[1]; $args.Count; (Get-Location).Path");

        var result = ClaudeCli.RunProcess("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "a b", "{\"c\":1}"], _dir.Path, TimeSpan.FromSeconds(30));

        var lines = result.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["a b", "{\"c\":1}", "2", _dir.Path], lines);
    }

    [Fact]
    public void RunProcess_closes_stdin_straight_away()
    {
        // ReadToEnd only returns once stdin is closed; if it wasn't, this would hang until the timeout.
        var result = ClaudeCli.RunProcess("powershell.exe", PowerShell("[Console]::In.ReadToEnd().Length"), _dir.Path, TimeSpan.FromSeconds(30));

        Assert.Equal("0", result.Output.Trim());
    }

    [Fact]
    public void RunProcess_reads_stderr_so_its_pipe_cannot_fill()
    {
        // Far more than a pipe buffer holds; a child blocked writing stderr would never exit.
        var result = ClaudeCli.RunProcess("powershell.exe",
            PowerShell("[Console]::Error.Write(('e' * 400000)); Write-Output 'done'"), _dir.Path, TimeSpan.FromSeconds(30));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("done", result.Output.Trim());
    }

    [Fact]
    public void RunProcess_keeps_at_most_one_megabyte_of_stdout_and_still_lets_the_child_finish()
    {
        var result = ClaudeCli.RunProcess("powershell.exe",
            PowerShell("[Console]::Out.Write(('y' * 2500000))"), _dir.Path, TimeSpan.FromSeconds(30));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ClaudeCli.MaxOutputBytes, result.Output.Length);
    }

    [Fact]
    public void RunProcess_kills_a_hung_child_at_the_timeout_and_throws()
    {
        var pidFile = _dir.File("child.pid");
        var script = $"Set-Content -Path '{pidFile}' -Value $PID; Start-Sleep -Seconds 120";

        var clock = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => ClaudeCli.RunProcess("powershell.exe", PowerShell(script), _dir.Path, TimeSpan.FromSeconds(8)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "it stopped at the timeout, not when the child finished");
        Assert.True(File.Exists(pidFile), "the child started and recorded its process id before the timeout (slow machine?)");
        var pid = int.Parse(File.ReadAllText(pidFile).Trim());
        Assert.True(Wait.Until(() => !IsRunning(pid)), $"process {pid} is still running");
    }

    [Fact]
    public void RunProcess_reports_a_program_that_cannot_start()
    {
        Assert.ThrowsAny<Exception>(() => ClaudeCli.RunProcess(_dir.File("missing.exe"), [], _dir.Path, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void RunUsage_creates_the_working_directory_and_returns_stdout()
    {
        var folder = _dir.File(@"made\here");

        var output = ClaudeCli.RunUsage("powershell.exe", PowerShell("(Get-Location).Path"), folder, TimeSpan.FromSeconds(30));

        Assert.True(Directory.Exists(folder));
        Assert.EndsWith(@"made\here", output.Trim());
    }

    [Fact]
    public void RunUsage_throws_naming_a_non_zero_exit_code_and_what_the_result_line_said()
    {
        var script = """Write-Output '{"type":"result","is_error":true,"result":"Not logged in"}'; exit 3""";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ClaudeCli.RunUsage("powershell.exe", PowerShell(script), _dir.Path, TimeSpan.FromSeconds(30)));

        Assert.Contains("3", ex.Message);
        Assert.Contains("Not logged in", ex.Message);
    }

    [Fact]
    public void The_usage_command_line_is_the_verified_one_with_one_argument_per_element() =>
        Assert.Equal(
            new[]
            {
                "-p", "/usage", "--no-session-persistence", "--setting-sources", "user", "--settings", """{"disableAllHooks":true}""",
                "--strict-mcp-config", "--output-format", "stream-json", "--verbose",
            },
            ClaudeCli.UsageArguments);

    [Fact]
    public void The_real_poll_gives_up_after_30_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(30), ClaudeCli.UsageTimeout);

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
