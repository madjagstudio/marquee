using System.Diagnostics;
using System.Text;
using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

public class StatusLinePipeTests
{
    static readonly TimeSpan Connect = TimeSpan.FromSeconds(2);

    // Each test uses its own pipe, so tests running in parallel never meet.
    static string UniquePipe() => "marquee-test-" + Guid.NewGuid().ToString("N");

    static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void Default_name_is_per_user() =>
        Assert.StartsWith("marquee-statusline-S-1-5-", StatusLinePipe.DefaultName);

    [Fact]
    public void A_sent_status_line_reaches_the_store()
    {
        var name = UniquePipe();
        var store = new SensorStore(() => Clock.T0);
        using var server = new StatusLineServer(store, new ListLog(), name, () => Clock.T0);
        Assert.True(server.Start());

        Assert.True(StatusLinePipe.TrySend(Fixture.Bytes("statusline/subscriber.json"), Connect, name));

        Assert.True(Wait.Until(() => Clock.Read(store, "claude.five_hour.pct") is 42.5));
        Assert.Equal(17.0, Clock.Read(store, "claude.seven_day.pct"));
    }

    [Fact]
    public void Several_messages_in_a_row_are_all_received()
    {
        var name = UniquePipe();
        var store = new SensorStore(() => Clock.T0);
        using var server = new StatusLineServer(store, new ListLog(), name, () => Clock.T0);
        server.Start();

        for (var pct = 1; pct <= 20; pct++)
            Assert.True(StatusLinePipe.TrySend(Json($"{{\"context_window\":{{\"used_percentage\":{pct}}}}}"), Connect, name));

        Assert.True(Wait.Until(() => Clock.Read(store, "claude.context.pct") is 20.0));
    }

    [Fact]
    public void Sending_with_nothing_listening_fails_quickly()
    {
        var clock = Stopwatch.StartNew();

        Assert.False(StatusLinePipe.TrySend(Json("{}"), TimeSpan.FromMilliseconds(100), UniquePipe()));

        Assert.True(clock.ElapsedMilliseconds < 1000, $"took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Empty_and_oversized_messages_are_not_sent() =>
        Assert.False(StatusLinePipe.TrySend([], Connect, UniquePipe()) || StatusLinePipe.TrySend(new byte[ClaudeStatusLine.MaxBytes + 1], Connect, UniquePipe()));

    [Fact]
    public void A_malformed_message_is_warned_about_once_and_the_next_good_one_works()
    {
        var name = UniquePipe();
        var log = new ListLog();
        var store = new SensorStore(() => Clock.T0);
        using var server = new StatusLineServer(store, log, name, () => Clock.T0);
        server.Start();

        StatusLinePipe.TrySend(Json("""{"rate_limits":{"five_hour":"full"}}"""), Connect, name);
        StatusLinePipe.TrySend(Json("""{"rate_limits":{"five_hour":"full"}}"""), Connect, name);
        Assert.True(Wait.Until(() => log.Entries.Count > 0));
        StatusLinePipe.TrySend(Fixture.Bytes("statusline/subscriber.json"), Connect, name);

        Assert.True(Wait.Until(() => Clock.Read(store, "claude.five_hour.pct") is 42.5));
        Assert.Equal(
            ["warn: Ignored a Claude Code status-line update: rate_limits.five_hour is not an object. The limits keep their last values."],
            log.Entries);
    }

    [Fact]
    public void An_oversized_message_from_another_client_is_ignored()
    {
        var name = UniquePipe();
        var log = new ListLog();
        var store = new SensorStore(() => Clock.T0);
        using var server = new StatusLineServer(store, log, name, () => Clock.T0);
        server.Start();

        using (var pipe = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.Out))
        {
            pipe.Connect(2000);
            pipe.Write(new byte[ClaudeStatusLine.MaxBytes + 100]);
        }

        Assert.True(Wait.Until(() => log.Entries.Count > 0));
        Assert.Equal(["warn: Ignored a Claude Code status-line update: a status-line message was larger than 64 KB. The limits keep their last values."], log.Entries);
        Assert.Empty(store.Snapshot().Keys);
    }

    [Fact]
    public void Dispose_stops_listening()
    {
        var name = UniquePipe();
        var server = new StatusLineServer(new SensorStore(), new ListLog(), name);
        server.Start();

        server.Dispose();

        Assert.False(StatusLinePipe.TrySend(Json("{}"), TimeSpan.FromMilliseconds(100), name));
    }
}
