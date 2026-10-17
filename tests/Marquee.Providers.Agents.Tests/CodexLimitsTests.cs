using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Codex;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

public class CodexLimitsTests : IDisposable
{
    const double FiveHourReset = 1792247400; // 14:30 UTC
    const double WeeklyReset = 1792573200;   // 10-21 09:00 UTC

    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    static string Line(string fixture, int index) => File.ReadAllLines(Fixture.Path($"codex/{fixture}"))[index];

    static string TokenCount(string rateLimits) =>
        $"{{\"timestamp\":\"2026-10-17T11:59:30.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":null,\"rate_limits\":{rateLimits}}}}}";

    string AddRollout(string fixture, string day, DateTime written)
    {
        var path = _dir.Write($"2026/10/{day}/rollout-{fixture}", File.ReadAllText(Fixture.Path($"codex/{fixture}")));
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    [Fact]
    public void Parses_a_weekly_only_plan()
    {
        var limits = CodexTokenCount.TryParse(Line("rollout-weekly.jsonl", 7));

        Assert.Equal(new CodexLimits(new CodexWindow(52, 10080, WeeklyReset), null, 1792238370), limits);
    }

    [Fact]
    public void Parses_a_plan_with_two_windows()
    {
        var limits = CodexTokenCount.TryParse(Line("rollout-two-limits.jsonl", 2));

        Assert.Equal(new CodexWindow(12, 300, FiveHourReset), limits!.Primary);
        Assert.Equal(new CodexWindow(30, 10080, WeeklyReset), limits.Secondary);
    }

    [Fact]
    public void Another_limits_event_is_skipped() => Assert.Null(CodexTokenCount.TryParse(Line("rollout-two-limits.jsonl", 3)));

    [Fact]
    public void Older_resets_in_seconds_counts_from_the_event_time()
    {
        var limits = CodexTokenCount.TryParse(Line("rollout-legacy.jsonl", 1));

        Assert.Equal(new CodexWindow(20, 300, 1792243800), limits!.Primary);
        Assert.Equal(new CodexWindow(7.5, 10080, WeeklyReset), limits.Secondary);
    }

    [Theory]
    [InlineData(0)] // session_meta
    [InlineData(1)] // task_started
    [InlineData(3)] // a message that mentions token_count
    public void Other_lines_are_not_limits(int index) => Assert.Null(CodexTokenCount.TryParse(Line("rollout-weekly.jsonl", index)));

    [Theory]
    [InlineData("{\"timestamp\":\"2026-10-17T11:59:30.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null,\"rate_limits\":{\"pri")]
    [InlineData("{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{},\"rate_limits\":null}}")]
    [InlineData("{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{}}}")]
    public void Partial_lines_and_events_without_limits_are_not_limits(string line) => Assert.Null(CodexTokenCount.TryParse(line));

    [Theory]
    [InlineData("{\"limit_id\":\"codex\",\"primary\":{\"percent\":50}}", "a Codex token_count event's rate_limits.primary has no used_percent number")]
    [InlineData("{\"primary\":\"50%\"}", "a Codex token_count event's rate_limits.primary is not an object")]
    [InlineData("{\"limit_id\":\"codex\",\"windows\":[]}", "a Codex token_count event's rate_limits has no primary window")]
    public void A_changed_shape_is_a_format_exception(string rateLimits, string message) =>
        Assert.Equal(message, Assert.Throws<FormatException>(() => CodexTokenCount.TryParse(TokenCount(rateLimits))).Message);

    [Fact]
    public void No_windows_at_all_is_valid() =>
        Assert.Equal(new CodexLimits(null, null, 1792238370), CodexTokenCount.TryParse(TokenCount("{\"limit_id\":\"codex\",\"primary\":null,\"secondary\":null}")));

    [Fact]
    public void Apply_writes_the_keys_until_each_window_resets()
    {
        var now = Clock.T0;
        var store = new SensorStore(() => now);

        CodexTokenCount.Apply(CodexTokenCount.TryParse(Line("rollout-two-limits.jsonl", 2))!, store, Clock.T0);

        Assert.Equal(12.0, Clock.Read(store, "codex.primary.pct"));
        Assert.Equal(FiveHourReset, Clock.Read(store, "codex.primary.resets_at"));
        Assert.Equal(300.0, Clock.Read(store, "codex.primary.window_min"));
        Assert.Equal(30.0, Clock.Read(store, "codex.secondary.pct"));
        Assert.Equal(10080.0, Clock.Read(store, "codex.secondary.window_min"));
        Assert.Equal(1792238400.0 - 600, Clock.Read(store, "codex.updated_at"));

        now = Clock.T0.AddHours(2.5);
        Assert.Null(Clock.Read(store, "codex.primary.pct"));
        Assert.Null(Clock.Read(store, "codex.primary.window_min"));
        Assert.Equal(30.0, Clock.Read(store, "codex.secondary.pct"));
    }

    [Fact]
    public void Provider_reads_the_newest_event_of_the_newest_log()
    {
        AddRollout("rollout-two-limits.jsonl", "17", new DateTime(2026, 10, 17, 11, 55, 0, DateTimeKind.Utc));
        AddRollout("rollout-weekly.jsonl", "17", new DateTime(2026, 10, 17, 11, 59, 31, DateTimeKind.Utc));
        var store = new SensorStore(() => Clock.T0);

        new CodexLimitsProvider(_dir.Path, () => Clock.T0).Poll(store);

        Assert.Equal(52.0, Clock.Read(store, "codex.primary.pct"));
        Assert.Null(Clock.Read(store, "codex.secondary.pct"));
    }

    [Fact]
    public void Provider_looks_further_back_when_the_newest_log_has_no_limits_yet()
    {
        AddRollout("rollout-two-limits.jsonl", "17", new DateTime(2026, 10, 17, 11, 55, 0, DateTimeKind.Utc));
        var fresh = _dir.Write("2026/10/17/rollout-fresh.jsonl", Line("rollout-weekly.jsonl", 0) + "\n" + Line("rollout-weekly.jsonl", 1) + "\n");
        File.SetLastWriteTimeUtc(fresh, new DateTime(2026, 10, 17, 11, 59, 0, DateTimeKind.Utc));
        var store = new SensorStore(() => Clock.T0);

        new CodexLimitsProvider(_dir.Path, () => Clock.T0).Poll(store);

        Assert.Equal(12.0, Clock.Read(store, "codex.primary.pct"));
    }

    [Fact]
    public void Provider_skips_logs_that_have_not_changed()
    {
        var path = AddRollout("rollout-weekly.jsonl", "17", new DateTime(2026, 10, 17, 11, 59, 31, DateTimeKind.Utc));
        var store = new SensorStore(() => Clock.T0);
        var provider = new CodexLimitsProvider(_dir.Path, () => Clock.T0);
        provider.Poll(store);

        // Rewrite the log in place with a different value but the same size and time: a poll that re-read it would see 99.
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"used_percent\":52.0", "\"used_percent\":99.0"));
        File.SetLastWriteTimeUtc(path, time);
        provider.Poll(store);

        Assert.Equal(52.0, Clock.Read(store, "codex.primary.pct"));
    }

    [Fact]
    public void Provider_without_codex_writes_nothing()
    {
        var store = new SensorStore(() => Clock.T0);

        new CodexLimitsProvider(_dir.File("missing"), () => Clock.T0).Poll(store);

        Assert.Empty(store.Snapshot().Keys);
    }

    [Fact]
    public void Provider_reports_a_format_change()
    {
        _dir.Write("2026/10/17/rollout-x.jsonl", TokenCount("{\"limit_id\":\"codex\",\"primary\":{\"percent\":50}}") + "\n");

        Assert.Throws<FormatException>(() => new CodexLimitsProvider(_dir.Path, () => Clock.T0).Poll(new SensorStore()));
    }
}
