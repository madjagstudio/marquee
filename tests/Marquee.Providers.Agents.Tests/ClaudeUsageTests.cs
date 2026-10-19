using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Marquee.Core.Processes;
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
        Assert.Equal(new ClaudeModelWindow("Example", 0, SevenDayReset), status.SevenDayModel);
        Assert.Null(status.ContextPct);
    }

    [Fact]
    public void Of_several_per_model_weekly_limits_the_most_used_one_is_read()
    {
        var status = ClaudeUsageReport.Parse(
            """{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[{"kind":"weekly_scoped","percent":20,"resets_at":"2026-10-24T10:00:00Z","scope":{"model":{"display_name":"Sonnet"}}},{"kind":"weekly_scoped","percent":61,"resets_at":"2026-10-24T10:00:00Z","scope":{"model":{"display_name":"Opus"}}},{"kind":"weekly_scoped","percent":40,"scope":null}]}}}}""");

        Assert.Equal(new ClaudeModelWindow("Opus", 61, SevenDayReset), status.SevenDayModel);
    }

    [Fact]
    public void A_per_model_weekly_limit_without_a_model_name_is_read_without_one()
    {
        var status = ClaudeUsageReport.Parse(
            """{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[{"kind":"weekly_scoped","percent":7,"scope":{"model":null}}]}}}}""");

        Assert.Equal(new ClaudeModelWindow(null, 7, null), status.SevenDayModel);
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
    public void Unknown_kinds_malformed_per_model_limits_and_entries_that_are_not_objects_are_ignored()
    {
        // A per-model limit is an extra: one Marquee can't read mustn't cost the 5-hour and weekly ones.
        var status = ClaudeUsageReport.Parse(
            """{"type":"assistant","message":{"usage_report":{"rate_limits":{"limits":[{"kind":"weekly_scoped","percent":"garbage"},{"kind":"something_new"},"text",null,{"kind":"weekly_all","percent":9,"resets_at":"2026-10-24T10:00:00Z"}]}}}}""");

        Assert.Null(status.FiveHour);
        Assert.Equal(new ClaudeWindow(9, SevenDayReset), status.SevenDay);
        Assert.Null(status.SevenDayModel);
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
            }, () => Now, () => Now - Clock.T0);
        }

        /// Polls at T0 + `minutes` and says whether it ran `claude`. The poll must fail with the no-limits message.
        public bool PollWithoutLimitsAt(double minutes)
        {
            Now = Clock.T0.AddMinutes(minutes);
            var before = Fetches;
            var ex = Assert.Throws<InvalidOperationException>(() => Provider.Poll(Store));
            Assert.Equal(ClaudeUsageProvider.NoLimitsMessage, ex.Message);
            return Fetches > before;
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
        Assert.Equal(0.0, Clock.Read(h.Store, "claude.seven_day_model.pct"));
        Assert.Equal(SevenDayReset, Clock.Read(h.Store, "claude.seven_day_model.resets_at"));
        Assert.Equal("Example", Clock.Read(h.Store, "claude.seven_day_model.name"));
        Assert.Equal(Clock.T0Unix, Clock.Read(h.Store, "claude.updated_at"));
        Assert.Null(Clock.Read(h.Store, "claude.context.pct"));

        h.Now = Clock.T0.AddHours(6); // past the five-hour reset, before the weekly one
        Assert.Null(Clock.Read(h.Store, "claude.five_hour.pct"));
        Assert.Null(Clock.Read(h.Store, "claude.five_hour.resets_at"));
        Assert.Equal(34.5, Clock.Read(h.Store, "claude.seven_day.pct"));
        Assert.Equal("Example", Clock.Read(h.Store, "claude.seven_day_model.name"));

        h.Now = Clock.T0.AddDays(8); // past the weekly reset: the model's name goes with its percentage
        Assert.Null(Clock.Read(h.Store, "claude.seven_day_model.pct"));
        Assert.Null(Clock.Read(h.Store, "claude.seven_day_model.name"));
    }

    [Theory]
    [InlineData("no-fresh-limits.jsonl")]
    [InlineData("api-key.jsonl")]
    public void A_poll_with_no_windows_writes_nothing_and_fails_saying_why(string fixture)
    {
        var h = new Harness { Fetch = () => Usage(fixture) };

        var ex = Assert.Throws<InvalidOperationException>(() => h.Provider.Poll(h.Store));

        Assert.Equal(
            "claude -p /usage listed no limits (an API-key login, or Claude Code couldn't fetch them and showed its last-known values); the last values stay until they reset.",
            ex.Message);
        Assert.Equal(1, h.Fetches);
        Assert.Null(Clock.Read(h.Store, "claude.updated_at"));
        Assert.Empty(h.Store.Snapshot().Keys);
    }

    [Fact]
    public void A_poll_with_no_windows_keeps_the_limits_already_in_the_store()
    {
        var h = new Harness { Fetch = () => Usage("subscriber.jsonl") };
        h.Provider.Poll(h.Store);

        h.Fetch = () => Usage("no-fresh-limits.jsonl");
        Assert.True(h.PollWithoutLimitsAt(ClaudeUsageProvider.DefaultInterval.TotalMinutes));

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
        h.Provider.Poll(h.Store);

        Assert.Equal(3, h.Fetches);
    }

    [Fact]
    public void The_wait_after_polls_without_limits_doubles_from_5_minutes_up_to_30() =>
        Assert.Equal(
            new[] { 5.0, 10, 20, 30, 30, 30 },
            Enumerable.Range(1, 6).Select(n => ClaudeUsageProvider.NoLimitsWait(n).TotalMinutes));

    [Fact]
    public void Polls_without_limits_back_off_5_10_20_then_30_minutes_and_rethrow_in_between()
    {
        var h = new Harness { Fetch = () => Usage("no-fresh-limits.jsonl") };

        // Each attempt, then a poll a second before the next one is due (only rethrows), then the due one (runs claude).
        Assert.True(h.PollWithoutLimitsAt(0));
        Assert.False(h.PollWithoutLimitsAt(5 - 1 / 60.0));
        Assert.True(h.PollWithoutLimitsAt(5));
        Assert.False(h.PollWithoutLimitsAt(15 - 1 / 60.0));
        Assert.True(h.PollWithoutLimitsAt(15));
        Assert.False(h.PollWithoutLimitsAt(35 - 1 / 60.0));
        Assert.True(h.PollWithoutLimitsAt(35));
        Assert.False(h.PollWithoutLimitsAt(65 - 1 / 60.0));
        Assert.True(h.PollWithoutLimitsAt(65));
        Assert.False(h.PollWithoutLimitsAt(95 - 1 / 60.0));
        Assert.True(h.PollWithoutLimitsAt(95)); // capped at 30

        Assert.Equal(6, h.Fetches);
        Assert.Empty(h.Store.Snapshot().Keys);
    }

    [Fact]
    public void While_waiting_the_same_no_limits_failure_is_rethrown()
    {
        var h = new Harness { Fetch = () => Usage("no-fresh-limits.jsonl") };
        var first = Assert.Throws<InvalidOperationException>(() => h.Provider.Poll(h.Store));

        h.Now = Clock.T0.AddMinutes(1);
        var second = Assert.Throws<InvalidOperationException>(() => h.Provider.Poll(h.Store));

        Assert.Same(first, second);
        Assert.Equal(1, h.Fetches);
    }

    [Fact]
    public void A_fresh_reading_resets_the_back_off_to_5_minutes()
    {
        var h = new Harness { Fetch = () => Usage("no-fresh-limits.jsonl") };
        Assert.True(h.PollWithoutLimitsAt(0));
        Assert.True(h.PollWithoutLimitsAt(5));
        Assert.True(h.PollWithoutLimitsAt(15)); // the next wait would be 20 minutes

        h.Now = Clock.T0.AddMinutes(35);
        h.Fetch = () => Usage("subscriber.jsonl");
        h.Provider.Poll(h.Store);
        Assert.Equal(12.0, Clock.Read(h.Store, "claude.five_hour.pct"));

        h.Fetch = () => Usage("no-fresh-limits.jsonl");
        Assert.True(h.PollWithoutLimitsAt(40));
        Assert.False(h.PollWithoutLimitsAt(45 - 1 / 60.0));
        Assert.True(h.PollWithoutLimitsAt(45)); // 5 minutes again, not 30
    }

    [Fact]
    public void Other_failures_wait_5_minutes_and_keep_the_no_limits_back_off()
    {
        var h = new Harness { Fetch = () => Usage("no-fresh-limits.jsonl") };
        Assert.True(h.PollWithoutLimitsAt(0));
        Assert.True(h.PollWithoutLimitsAt(5)); // next wait 10

        h.Now = Clock.T0.AddMinutes(15);
        h.Fetch = () => throw new TimeoutException("slow");
        Assert.Throws<TimeoutException>(() => h.Provider.Poll(h.Store));
        h.Now = Clock.T0.AddMinutes(20) - TimeSpan.FromSeconds(1);
        Assert.Throws<TimeoutException>(() => h.Provider.Poll(h.Store));
        Assert.Equal(3, h.Fetches);

        h.Fetch = () => Usage("no-fresh-limits.jsonl");
        Assert.True(h.PollWithoutLimitsAt(20)); // a failure isn't a fresh reading: the next wait is 20
        Assert.False(h.PollWithoutLimitsAt(40 - 1 / 60.0));
        Assert.True(h.PollWithoutLimitsAt(40));
    }

    [Fact]
    public void The_runner_logs_no_limits_once_and_says_when_limits_are_back()
    {
        // ProviderRunner retries a failing provider every few milliseconds here; the provider rethrows without running
        // claude, and the runner logs a repeated message once.
        long minutes = 0;
        var fetches = 0;
        var fetch = (Func<string>)(() => Usage("no-fresh-limits.jsonl"));
        var provider = new ClaudeUsageProvider(() =>
        {
            Interlocked.Increment(ref fetches);
            return Volatile.Read(ref fetch)();
        }, () => Clock.T0, () => TimeSpan.FromMinutes(Interlocked.Read(ref minutes)));
        var log = new ListLog();
        var store = new SensorStore(() => Clock.T0);

        using (var runner = new ProviderRunner(store, log, firstRetry: TimeSpan.FromMilliseconds(5)))
        {
            runner.Start(provider);
            Assert.True(Wait.Until(() => log.Entries.Count >= 1));
            Thread.Sleep(300); // several retries
            Assert.Equal(1, Volatile.Read(ref fetches));

            Volatile.Write(ref fetch, () => Usage("subscriber.jsonl"));
            Interlocked.Exchange(ref minutes, 5);
            Assert.True(Wait.Until(() => log.Entries.Any(e => e.Contains("working again"))));
        }

        var errors = log.Entries.Where(e => e.StartsWith("error:")).ToList();
        Assert.Single(errors);
        Assert.Contains(ClaudeUsageProvider.NoLimitsMessage, errors[0]);
        Assert.Equal(12.0, Clock.Read(store, "claude.five_hour.pct"));
    }

    // ---- The real fetch ----

    sealed class FetchHarness
    {
        public string? Exe = @"C:\Users\test\.local\bin\claude.exe";
        public bool Elevated;
        public readonly List<(string Runner, string Exe, IReadOnlyList<string> Arguments, string Directory, TimeSpan Timeout)> Runs = [];
        public readonly ListLog Log = new();
        public Func<string> Fetch { get; }

        public FetchHarness(string workingDirectory) =>
            Fetch = ClaudeUsageProvider.RealFetch(Log, () => Exe, () => Elevated, workingDirectory, Runner("normal"), Runner("unelevated"));

        ProcessRunner Runner(string name) => (exe, arguments, directory, timeout) =>
        {
            Runs.Add((name, exe, arguments, directory, timeout));
            return new ProcessResult(0, $"output of {name}");
        };
    }

    [Fact]
    public void The_real_fetch_runs_claude_normally_when_Marquee_is_not_elevated()
    {
        var h = new FetchHarness(_dir.File("data"));

        Assert.Equal("output of normal", h.Fetch());

        var run = Assert.Single(h.Runs);
        Assert.Equal("normal", run.Runner);
        Assert.Equal(h.Exe, run.Exe);
        Assert.Equal(ClaudeCli.UsageArguments, run.Arguments);
        Assert.Equal(_dir.File("data"), run.Directory);
        Assert.Equal(ClaudeCli.UsageTimeout, run.Timeout);
        Assert.True(Directory.Exists(_dir.File("data")));
    }

    [Fact]
    public void The_real_fetch_runs_claude_unelevated_when_Marquee_is_elevated()
    {
        var h = new FetchHarness(_dir.Path) { Elevated = true };

        Assert.Equal("output of unelevated", h.Fetch());

        Assert.Equal("unelevated", Assert.Single(h.Runs).Runner);
    }

    [Fact]
    public void The_real_fetch_fails_without_running_anything_when_claude_is_not_found()
    {
        var h = new FetchHarness(_dir.Path) { Exe = null };

        var ex = Assert.Throws<InvalidOperationException>(() => h.Fetch());

        Assert.Equal(
            "claude.exe wasn't found on PATH, in ~/.local/bin or in the desktop app; Claude Code limits come only from the status line.",
            ex.Message);
        Assert.Empty(h.Runs);
        Assert.Empty(h.Log.Entries);
    }

    [Fact]
    public void The_real_fetch_logs_which_claude_it_runs_once_and_again_if_it_changes()
    {
        var h = new FetchHarness(_dir.Path);

        h.Fetch();
        h.Fetch();
        var desktopApp = @"C:\Users\test\AppData\Roaming\Claude\claude-code\2.1.300\f0584be0fbaf\claude.exe";
        h.Exe = desktopApp;
        h.Fetch();

        Assert.Equal(
            [
                @"info: Claude Code limits: running C:\Users\test\.local\bin\claude.exe for /usage.",
                $"info: Claude Code limits: running {desktopApp} for /usage.",
            ],
            h.Log.Entries);
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
    public void PATH_entries_that_are_not_full_paths_are_skipped()
    {
        // A relative entry would be looked up from Marquee's current folder, which anyone may have chosen.
        var pc = new FakePc();
        pc.Environment["PATH"] = @"bin;.\tools;\rooted\no-drive;C:relative;""tools2"";C:\Tools";
        pc.Files.Add(@"bin\claude.exe");
        pc.Files.Add(@".\tools\claude.exe");
        pc.Files.Add(@"\rooted\no-drive\claude.exe");
        pc.Files.Add(@"C:relative\claude.exe");
        pc.Files.Add(@"tools2\claude.exe");
        pc.Files.Add(@"C:\Tools\claude.exe");

        Assert.Equal(@"C:\Tools\claude.exe", pc.Find());
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
        Assert.Throws<Win32Exception>(() => ClaudeCli.RunProcess(_dir.File("missing.exe"), [], _dir.Path, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void RunProcess_turns_off_Claude_Codes_autoupdater_in_the_child()
    {
        // Marquee's own value must not decide it (a Claude Code session running these tests sets it to 1 already).
        var saved = Environment.GetEnvironmentVariable("DISABLE_AUTOUPDATER");
        Environment.SetEnvironmentVariable("DISABLE_AUTOUPDATER", "0");
        try
        {
            var result = ClaudeCli.RunProcess("powershell.exe", PowerShell("$env:DISABLE_AUTOUPDATER"), _dir.Path, TimeSpan.FromSeconds(30));

            Assert.Equal("1", result.Output.Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISABLE_AUTOUPDATER", saved);
        }
    }

    [Fact]
    public async Task RunProcess_puts_the_child_in_the_job_that_dies_with_Marquee()
    {
        var pidFile = _dir.File("child.pid");
        var script = $"Set-Content -Path '{pidFile}' -Value $PID; Start-Sleep -Seconds 3";

        var inJob = false;
        ChildProcessJob.Assigned.Value = (job, process) => inJob = job.Contains(process);
        var running = Task.Run(() => ClaudeCli.RunProcess("powershell.exe", PowerShell(script), _dir.Path, TimeSpan.FromSeconds(30)));

        WaitForPid(pidFile);
        Assert.True(inJob, "the child is not in a job of its own");
        Assert.Equal(0, (await running).ExitCode);
    }

    /// The process id a child script wrote to the file, once it has.
    internal static int WaitForPid(string pidFile)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            try
            {
                if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid)) return pid;
            }
            catch (IOException)
            {
                // Still being written.
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"no process id in {pidFile} after 20 seconds");
    }

    [Fact]
    public void RunUsage_creates_the_working_directory_and_returns_stdout()
    {
        var folder = _dir.File(@"made\here");

        var output = ClaudeCli.RunUsage("powershell.exe", PowerShell("(Get-Location).Path"), folder, TimeSpan.FromSeconds(30), ClaudeCli.RunProcess);

        Assert.True(Directory.Exists(folder));
        Assert.EndsWith(@"made\here", output.Trim());
    }

    [Fact]
    public void RunUsage_throws_naming_a_non_zero_exit_code_and_what_the_result_line_said()
    {
        var script = """Write-Output '{"type":"result","is_error":true,"result":"Not logged in"}'; exit 3""";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ClaudeCli.RunUsage("powershell.exe", PowerShell(script), _dir.Path, TimeSpan.FromSeconds(30), ClaudeCli.RunProcess));

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
