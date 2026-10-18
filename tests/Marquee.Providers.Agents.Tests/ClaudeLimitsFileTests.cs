using System.Text.Json.Nodes;
using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

/// The fixture `limits/claude-limits.json` is what Marquee saves after the `statusline/subscriber.json` update at T0:
/// five-hour 42.5% until 14:30 (T0 + 2.5 h), weekly 17% until T0 + 93.75 h, read at T0.
public class ClaudeLimitsFileTests
{
    static readonly string[] LimitKeys =
        ["claude.five_hour.pct", "claude.five_hour.resets_at", "claude.seven_day.pct", "claude.seven_day.resets_at", "claude.updated_at"];

    static ClaudeLimitsFile Saved(TempDir dir, ListLog log) =>
        new(dir.Write("claude-limits.json", Fixture.Text("limits/claude-limits.json")), log);

    [Fact]
    public void A_restart_shows_the_saved_limits_with_their_real_age()
    {
        using var dir = new TempDir();
        var log = new ListLog();
        var now = Clock.T0.AddHours(1);
        var store = new SensorStore(() => now);

        Saved(dir, log).Restore(store, now);

        Assert.Equal(42.5, Clock.Read(store, "claude.five_hour.pct"));
        Assert.Equal(1792247400.0, Clock.Read(store, "claude.five_hour.resets_at"));
        Assert.Equal(17.0, Clock.Read(store, "claude.seven_day.pct"));
        Assert.Equal(1792573200.0, Clock.Read(store, "claude.seven_day.resets_at"));
        Assert.Equal(Clock.T0Unix, Clock.Read(store, "claude.updated_at")); // read at T0, not at the restart
        Assert.Null(Clock.Read(store, "claude.context.pct"));
        Assert.Equal("info", Assert.Single(log.Entries).Split(':')[0]);
    }

    [Fact]
    public void Restored_windows_still_expire_at_their_reset()
    {
        using var dir = new TempDir();
        var now = Clock.T0.AddHours(1);
        var store = new SensorStore(() => now);
        Saved(dir, new ListLog()).Restore(store, now);

        now = Clock.T0.AddHours(2.5);

        Assert.Null(Clock.Read(store, "claude.five_hour.pct"));
        Assert.Null(Clock.Read(store, "claude.five_hour.resets_at"));
        Assert.Equal(17.0, Clock.Read(store, "claude.seven_day.pct"));
    }

    [Fact]
    public void A_window_that_reset_while_stopped_stays_stale()
    {
        using var dir = new TempDir();
        var now = Clock.T0.AddHours(3); // the five-hour window reset at T0 + 2.5 h
        var store = new SensorStore(() => now);

        Saved(dir, new ListLog()).Restore(store, now);

        Assert.Null(Clock.Read(store, "claude.five_hour.pct"));
        Assert.Null(Clock.Read(store, "claude.five_hour.resets_at"));
        Assert.Equal(17.0, Clock.Read(store, "claude.seven_day.pct"));
        Assert.Equal(Clock.T0Unix, Clock.Read(store, "claude.updated_at"));
    }

    [Fact]
    public void When_every_window_has_reset_nothing_is_restored_not_even_the_time()
    {
        using var dir = new TempDir();
        var log = new ListLog();
        var now = Clock.T0.AddDays(5);
        var store = new SensorStore(() => now);

        Saved(dir, log).Restore(store, now);

        Assert.Empty(store.Snapshot().Keys);
        Assert.EndsWith("is still current.", Assert.Single(log.Entries));
    }

    [Fact]
    public void A_file_saved_with_no_limits_restores_nothing_and_is_not_a_problem()
    {
        using var dir = new TempDir();
        var log = new ListLog();
        var store = new SensorStore(() => Clock.T0);

        new ClaudeLimitsFile(dir.Write("claude-limits.json", "{}\n"), log).Restore(store, Clock.T0);

        Assert.Empty(store.Snapshot().Keys);
        Assert.StartsWith("info:", Assert.Single(log.Entries));
    }

    [Fact]
    public void Live_readings_after_the_restore_win()
    {
        using var dir = new TempDir();
        var now = Clock.T0.AddHours(1);
        var store = new SensorStore(() => now);
        Saved(dir, new ListLog()).Restore(store, now);

        ClaudeStatusLine.Apply(new ClaudeStatus(new ClaudeWindow(55, 1792247400), null, null), store, now);

        Assert.Equal(55.0, Clock.Read(store, "claude.five_hour.pct"));
        Assert.Equal(17.0, Clock.Read(store, "claude.seven_day.pct"));
        Assert.Equal(Clock.T0Unix + 3600, Clock.Read(store, "claude.updated_at"));
    }

    [Fact]
    public void A_missing_file_is_ignored_with_one_log_line()
    {
        using var dir = new TempDir();
        var log = new ListLog();
        var store = new SensorStore(() => Clock.T0);

        new ClaudeLimitsFile(dir.File("claude-limits.json"), log).Restore(store, Clock.T0);

        Assert.Empty(store.Snapshot().Keys);
        Assert.StartsWith("info: Claude Code limits: nothing saved", Assert.Single(log.Entries));
    }

    [Theory]
    [InlineData("limits/truncated.json", "it is not valid JSON")]
    [InlineData(null, "it is not a JSON object")]
    public void A_corrupt_file_is_ignored_with_one_log_line(string? fixture, string problem)
    {
        using var dir = new TempDir();
        var log = new ListLog();
        var store = new SensorStore(() => Clock.T0);
        var path = dir.Write("claude-limits.json", fixture is null ? "[42.5, 17]" : Fixture.Text(fixture));

        new ClaudeLimitsFile(path, log).Restore(store, Clock.T0);

        Assert.Empty(store.Snapshot().Keys);
        var line = Assert.Single(log.Entries);
        Assert.StartsWith("warn: Claude Code limits: ignoring", line);
        Assert.Contains(problem, line);
    }

    [Theory]
    [InlineData("""{"five_hour":"42%","updated_at":1792238400}""", "five_hour is not a pct and resets_at pair")]
    [InlineData("""{"five_hour":{"pct":"42.5","resets_at":1792247400},"updated_at":1792238400}""", "five_hour is not a pct and resets_at pair")]
    [InlineData("""{"seven_day":{"pct":17},"updated_at":1792238400}""", "seven_day is not a pct and resets_at pair")]
    [InlineData("""{"seven_day":{"pct":17,"resets_at":-5},"updated_at":1792238400}""", "seven_day is not a pct and resets_at pair")]
    [InlineData("""{"five_hour":{"pct":42.5,"resets_at":1792247400}}""", "it has no updated_at time")]
    [InlineData("""{"five_hour":{"pct":42.5,"resets_at":1792247400},"updated_at":"recently"}""", "it has no updated_at time")]
    public void A_file_in_the_wrong_shape_is_ignored_whole(string json, string problem)
    {
        using var dir = new TempDir();
        var log = new ListLog();
        var store = new SensorStore(() => Clock.T0);

        new ClaudeLimitsFile(dir.Write("claude-limits.json", json), log).Restore(store, Clock.T0);

        Assert.Empty(store.Snapshot().Keys);
        Assert.Equal($"warn: Claude Code limits: ignoring {dir.File("claude-limits.json")} ({problem}).", Assert.Single(log.Entries));
    }

    [Fact]
    public void Save_writes_what_restore_reads()
    {
        using var dir = new TempDir();
        var path = dir.File("Marquee/claude-limits.json"); // the folder doesn't exist yet
        var store = new SensorStore(() => Clock.T0);
        ClaudeStatusLine.Apply(ClaudeStatusLine.Parse(Fixture.Bytes("statusline/subscriber.json")), store, Clock.T0);

        new ClaudeLimitsFile(path, new ListLog()).Save(store.Snapshot());

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Fixture.Text("limits/claude-limits.json")), JsonNode.Parse(File.ReadAllText(path))));
        var restored = new SensorStore(() => Clock.T0);
        new ClaudeLimitsFile(path, new ListLog()).Restore(restored, Clock.T0);
        foreach (var key in LimitKeys)
            Assert.Equal(Clock.Read(store, key), Clock.Read(restored, key));
    }

    [Fact]
    public void The_file_holds_only_the_limit_values()
    {
        using var dir = new TempDir();
        var path = dir.File("claude-limits.json");
        var store = new SensorStore(() => Clock.T0);
        ClaudeStatusLine.Apply(ClaudeStatusLine.Parse(Fixture.Bytes("statusline/subscriber.json")), store, Clock.T0);
        store.Set("claude.sessions.total", 2);
        store.Set("codex.five_hour.pct", 9);
        store.Set("custom.note", "example");

        new ClaudeLimitsFile(path, new ListLog()).Save(store.Snapshot());

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(["five_hour", "seven_day", "updated_at"], root.Select(p => p.Key).Order());
        foreach (var window in new[] { "five_hour", "seven_day" })
            Assert.Equal(["pct", "resets_at"], root[window]!.AsObject().Select(p => p.Key).Order());
    }

    [Fact]
    public void Save_leaves_out_windows_without_a_reset_time_and_expired_ones()
    {
        var now = Clock.T0;
        var store = new SensorStore(() => now);
        ClaudeStatusLine.Apply(new ClaudeStatus(new ClaudeWindow(12, Clock.T0Unix + 60), new ClaudeWindow(30, null), null), store, Clock.T0);

        Assert.Contains("five_hour", ClaudeLimitsFile.Serialize(store.Snapshot()));
        Assert.DoesNotContain("seven_day", ClaudeLimitsFile.Serialize(store.Snapshot()));

        now = Clock.T0.AddMinutes(1); // the five-hour window resets
        Assert.Equal("{}", ClaudeLimitsFile.Serialize(store.Snapshot()).Trim());
    }

    [Fact]
    public void Save_rewrites_the_file_only_when_the_limits_change()
    {
        using var dir = new TempDir();
        var path = dir.File("claude-limits.json");
        var now = Clock.T0;
        var store = new SensorStore(() => now);
        var file = new ClaudeLimitsFile(path, new ListLog());
        ClaudeStatusLine.Apply(new ClaudeStatus(new ClaudeWindow(12, Clock.T0Unix + 3600), null, null), store, Clock.T0);
        file.Save(store.Snapshot());
        File.Delete(path);

        now = Clock.T0.AddSeconds(30);
        file.Save(store.Snapshot());
        Assert.False(File.Exists(path));

        ClaudeStatusLine.Apply(new ClaudeStatus(new ClaudeWindow(13, Clock.T0Unix + 3600), null, null), store, now);
        file.Save(store.Snapshot());
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_failed_save_is_logged_once_and_never_throws()
    {
        using var dir = new TempDir();
        var path = dir.File("claude-limits.json");
        Directory.CreateDirectory(path); // a folder where the file should be
        var log = new ListLog();
        var now = Clock.T0;
        var store = new SensorStore(() => now);
        var file = new ClaudeLimitsFile(path, log);

        for (var i = 0; i < 3; i++)
        {
            ClaudeStatusLine.Apply(new ClaudeStatus(new ClaudeWindow(10 + i, Clock.T0Unix + 3600), null, null), store, now);
            file.Save(store.Snapshot());
        }

        Assert.StartsWith("warn: Claude Code limits: can't save them", Assert.Single(log.Entries));
    }
}
