using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Codex;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

public class CodexSessionsProviderTests : IDisposable
{
    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// A copy of a fixture log, last written `minutesAgo` before Clock.T0.
    string Add(string fixture, double minutesAgo, string? name = null)
    {
        var path = _dir.Write($"2026/10/17/{name ?? fixture}", File.ReadAllText(Fixture.Path($"codex/{fixture}")));
        File.SetLastWriteTimeUtc(path, Clock.T0.UtcDateTime.AddMinutes(-minutesAgo));
        return path;
    }

    (object? Total, object? Busy, object? Idle) Poll(CodexSessionsProvider? provider = null)
    {
        var store = new SensorStore(() => Clock.T0);
        (provider ?? new CodexSessionsProvider(_dir.Path, () => Clock.T0)).Poll(store);
        return (Clock.Read(store, "codex.sessions.total"), Clock.Read(store, "codex.sessions.busy"), Clock.Read(store, "codex.sessions.idle"));
    }

    [Fact]
    public void Counts_recent_logs_by_their_last_turn_event()
    {
        Add("rollout-weekly.jsonl", 1);      // ends with task_complete: idle
        Add("rollout-two-limits.jsonl", 2);  // task_started, no end yet: busy
        Add("rollout-aborted.jsonl", 3);     // turn_aborted: idle

        Assert.Equal((3.0, 1.0, 2.0), Poll());
    }

    [Fact]
    public void Logs_older_than_ten_minutes_are_not_sessions()
    {
        Add("rollout-weekly.jsonl", 9.9);
        Add("rollout-two-limits.jsonl", 10.1);

        Assert.Equal((1.0, 0.0, 1.0), Poll());
    }

    [Fact]
    public void Sub_agent_logs_are_not_counted()
    {
        Add("rollout-weekly.jsonl", 1);
        Add("rollout-subagent.jsonl", 1);

        Assert.Equal((1.0, 0.0, 1.0), Poll());
    }

    [Fact]
    public void A_log_without_turn_events_in_its_tail_is_busy()
    {
        var path = Add("rollout-weekly.jsonl", 1);
        File.AppendAllText(path, string.Concat(Enumerable.Repeat("{\"type\":\"response_item\",\"payload\":{\"type\":\"reasoning\",\"text\":\"" + new string('x', 1000) + "\"}}\n", 1200)));
        File.SetLastWriteTimeUtc(path, Clock.T0.UtcDateTime.AddMinutes(-1));

        Assert.Equal((1.0, 1.0, 0.0), Poll());
    }

    [Fact]
    public void A_log_with_an_unreadable_first_line_still_counts()
    {
        var path = _dir.Write("2026/10/17/rollout-odd.jsonl", "not json\n{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}\n");
        File.SetLastWriteTimeUtc(path, Clock.T0.UtcDateTime.AddMinutes(-1));

        Assert.Equal((1.0, 0.0, 1.0), Poll());
    }

    [Fact]
    public void A_sub_agent_log_seen_before_its_first_line_is_complete_is_recognized_later()
    {
        var full = File.ReadAllText(Fixture.Path("codex/rollout-subagent.jsonl"));
        var path = _dir.Write("2026/10/17/rollout-subagent.jsonl", full[..100]); // session_meta not finished: no newline yet
        File.SetLastWriteTimeUtc(path, Clock.T0.UtcDateTime.AddMinutes(-1));
        var provider = new CodexSessionsProvider(_dir.Path, () => Clock.T0);
        Assert.Equal(1.0, Poll(provider).Total); // unknown for now, so it counts

        File.WriteAllText(path, full);
        File.SetLastWriteTimeUtc(path, Clock.T0.UtcDateTime.AddMinutes(-1));

        Assert.Equal(0.0, Poll(provider).Total);
    }

    [Fact]
    public void A_session_that_finishes_its_turn_turns_idle()
    {
        var path = Add("rollout-two-limits.jsonl", 1);
        var provider = new CodexSessionsProvider(_dir.Path, () => Clock.T0);
        Assert.Equal((1.0, 1.0, 0.0), Poll(provider));

        File.AppendAllText(path, "{\"timestamp\":\"2026-10-17T11:59:59.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}\n");
        File.SetLastWriteTimeUtc(path, Clock.T0.UtcDateTime);

        Assert.Equal((1.0, 0.0, 1.0), Poll(provider));
    }

    [Fact]
    public void No_codex_folder_means_no_sessions() =>
        Assert.Equal((0.0, 0.0, 0.0), Poll(new CodexSessionsProvider(_dir.File("missing"), () => Clock.T0)));
}
