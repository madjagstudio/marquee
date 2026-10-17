using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
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
    public void Empty_and_oversized_messages_are_not_sent()
    {
        Assert.False(StatusLinePipe.TrySend([], Connect, UniquePipe()), "an empty message was sent");
        Assert.False(StatusLinePipe.TrySend(new byte[ClaudeStatusLine.MaxBytes + 1], Connect, UniquePipe()), "an oversized message was sent");
    }

    [Fact]
    public async Task Sending_to_a_server_that_never_reads_gives_up_at_the_timeout()
    {
        var name = UniquePipe();
        using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.In, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        _ = server.WaitForConnectionAsync();
        var clock = Stopwatch.StartNew();

        // Run off-thread so a hang fails the test (instead of hanging the run).
        var send = Task.Run(() => StatusLinePipe.TrySend(new byte[60_000], TimeSpan.FromMilliseconds(300), name));
        var finished = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5))) == send;

        Assert.True(finished, "TrySend hung writing to a server that never reads");
        Assert.False(await send);
        Assert.True(clock.ElapsedMilliseconds < 2000, $"took {clock.ElapsedMilliseconds} ms");
    }

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
    public void Dispose_during_a_stalled_message_is_clean()
    {
        var name = UniquePipe();
        var server = new StatusLineServer(new SensorStore(), new ListLog(), name);
        server.Start();
        using var stalled = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.Out);
        stalled.Connect(2000);
        Thread.Sleep(200); // the server is now waiting for the rest of the message

        server.Dispose();
        server.Dispose();

        var clock = Stopwatch.StartNew();
        Assert.False(StatusLinePipe.TrySend(Json("{}"), TimeSpan.FromMilliseconds(100), name));
        Assert.True(clock.ElapsedMilliseconds < 1000, $"took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void The_pipe_is_owned_by_the_user_and_open_only_to_the_user()
    {
        var name = UniquePipe();
        using var server = new StatusLineServer(new SensorStore(), new ListLog(), name);
        Assert.True(server.Start());

        using var client = new NamedPipeClientStream(".", name, PipeDirection.Out);
        client.Connect(2000);
        var security = client.GetAccessControl();

        var user = WindowsIdentity.GetCurrent().User;
        Assert.Equal(user, security.GetOwner(typeof(SecurityIdentifier)));
        var rule = Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>());
        Assert.Equal((user, AccessControlType.Allow, PipeAccessRights.FullControl), (rule.IdentityReference, rule.AccessControlType, rule.PipeAccessRights));
    }

    [Fact]
    public void Server_security_names_the_user_not_the_token_owner()
    {
        // An elevated administrator's token owner is BUILTIN\Administrators, which the user's non-elevated bridge can't
        // use; the user's own SID is the one both sides share.
        var user = new SecurityIdentifier("S-1-5-21-1000-2000-3000-1001");

        var security = StatusLinePipe.ServerSecurity(user);

        Assert.Equal(user, security.GetOwner(typeof(SecurityIdentifier)));
        var rule = Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>());
        Assert.Equal((user, AccessControlType.Allow, PipeAccessRights.FullControl), (rule.IdentityReference, rule.AccessControlType, rule.PipeAccessRights));
    }

    [Fact]
    public void The_bridge_refuses_a_pipe_owned_by_someone_else()
    {
        var name = UniquePipe();
        var store = new SensorStore(() => Clock.T0);
        using var server = new StatusLineServer(store, new ListLog(), name, () => Clock.T0);
        server.Start();
        var someoneElse = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        Assert.False(StatusLinePipe.TrySend(Fixture.Bytes("statusline/subscriber.json"), Connect, name, someoneElse));

        // Messages are handled in order: once the next one is in, the refused one would have been too.
        Assert.True(StatusLinePipe.TrySend(Json("""{"context_window":{"used_percentage":7}}"""), Connect, name));
        Assert.True(Wait.Until(() => Clock.Read(store, "claude.context.pct") is 7.0));
        Assert.Null(Clock.Read(store, "claude.five_hour.pct"));
    }

    [Fact]
    public void The_server_refuses_a_pipe_someone_else_created_first()
    {
        // Another user can create the pipe name first and let anyone add instances; Marquee would then be serving that
        // user's pipe. Simulated by expecting a different owner than the one the pipe gets.
        var someoneElse = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        Assert.Throws<UnauthorizedAccessException>(() => StatusLinePipe.CreateServer(UniquePipe(), someoneElse));
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
