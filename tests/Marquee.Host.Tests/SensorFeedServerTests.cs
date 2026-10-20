using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Marquee.Core.Pipes;
using Marquee.Core.Sensors;
using Marquee.Host.SensorFeed;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

/// The server writes on the thread pool, and a read completes only once it has written the message. On a loaded CI
/// runner the pool can take seconds to get to the server, so reads that must succeed get a generous limit; they return
/// as soon as the server answers. The limits a test checks are kept short, and the bounds on how long those take leave
/// room for the same delays.
public class SensorFeedServerTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// Upper bound for a call given a short timeout: well past the timeout, far short of a hang.
    static readonly TimeSpan Quickly = TimeSpan.FromSeconds(10);

    // Each test uses pipes of its own, so tests running in parallel never meet, and the tray's own are never touched.
    static SensorFeedPipes UniquePipes()
    {
        var id = Guid.NewGuid().ToString("N");
        return new SensorFeedPipes($"marquee-test-values-{id}", $"marquee-test-history-{id}");
    }

    /// Fast limits for tests: the real caps, a short write timeout, and the real rate.
    static SensorFeedLimits Limits(TimeSpan? writeTimeout = null) =>
        SensorFeedLimits.Default with { WriteTimeout = writeTimeout ?? TimeSpan.FromMilliseconds(300) };

    static SensorFeedServer Server(SensorStore store, SensorFeedPipes pipes, TimeProvider? time = null, SensorFeedLimits? limits = null,
        ListLog? log = null)
    {
        var server = new SensorFeedServer(store, log ?? new ListLog(), time ?? TimeProvider.System, pipes, limits ?? Limits());
        server.Start();
        return server;
    }

    /// Enough text that the message can't sit whole in the pipe's buffers: a write blocks until the client reads it.
    static SensorStore BigStore()
    {
        var store = new SensorStore(() => T0);
        for (var i = 0; i < 1000; i++) store.Set($"custom.text{i:D4}", new string('x', 250));
        return store;
    }

    [Fact]
    public void The_default_pipes_are_named_for_the_user()
    {
        var user = WindowsIdentity.GetCurrent().User!.Value;

        Assert.Equal(($"Marquee.Values.{user}", $"Marquee.History.{user}"),
            (SensorFeedPipes.Default.Values, SensorFeedPipes.Default.History));
    }

    [Fact]
    public void A_client_reads_every_value_with_its_stale_flag_and_the_stores_now()
    {
        var now = T0;
        var store = new SensorStore(() => now);
        store.Set("cpu.temp", 52.5);
        store.Set("cpu.name", "Ryzen 9");
        store.Set("gpu.temp", 61, TimeSpan.FromSeconds(5));
        store.Set("custom.build", "green"); // as `marquee push` writes it
        now = T0.AddSeconds(10);
        var pipes = UniquePipes();
        using var server = Server(store, pipes);

        var values = new SensorFeedClient(pipes).TryReadValues(Patience);

        Assert.NotNull(values);
        Assert.Equal((SensorFeedFormat.Version, T0.AddSeconds(10), false), (values.Version, values.Now, values.Truncated));
        Assert.Equal(["cpu.name", "cpu.temp", "custom.build", "gpu.temp"], values.Values.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new FeedValue(52.5, null, false), values.Values["cpu.temp"]);
        Assert.Equal(new FeedValue(null, "Ryzen 9", false), values.Values["cpu.name"]);
        Assert.Equal(new FeedValue(61, null, true), values.Values["gpu.temp"]);
        Assert.Equal(new FeedValue(null, "green", false), values.Values["custom.build"]);
    }

    [Fact]
    public void A_client_reads_the_history_of_every_numeric_key()
    {
        var store = new SensorStore(() => T0, historyLength: 3);
        foreach (var temp in new[] { 50.0, 51, 52, 53.25 }) store.Set("cpu.temp", temp);
        store.Set("gpu.load", 7);
        store.Set("cpu.name", "Ryzen 9");
        var pipes = UniquePipes();
        using var server = Server(store, pipes);

        var history = new SensorFeedClient(pipes).TryReadHistory(Patience);

        Assert.NotNull(history);
        Assert.Equal((SensorFeedFormat.Version, T0, false), (history.Version, history.Now, history.Truncated));
        Assert.Equal(["cpu.temp", "gpu.load"], history.History.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([51.0, 52, 53.25], history.History["cpu.temp"]);
        Assert.Equal([7.0], history.History["gpu.load"]);
    }

    [Fact]
    public void Each_read_sees_the_store_as_it_is_then()
    {
        var store = new SensorStore(() => T0);
        var pipes = UniquePipes();
        using var server = Server(store, pipes);
        var client = new SensorFeedClient(pipes);

        for (var i = 1; i <= 3; i++)
        {
            store.Set("cpu.load", i);
            Assert.Equal(i, client.TryReadValues(Patience)?.Values["cpu.load"].Number);
        }
    }

    [Fact]
    public void With_no_server_the_client_returns_null_quickly()
    {
        var client = new SensorFeedClient(UniquePipes());
        var clock = Stopwatch.StartNew();

        Assert.Null(client.TryReadValues(TimeSpan.FromMilliseconds(100)));
        Assert.Null(client.TryReadHistory(TimeSpan.FromMilliseconds(100)));

        Assert.True(clock.Elapsed < Quickly, $"took {clock.Elapsed}");
    }

    [Fact]
    public void Each_pipe_is_owned_by_the_user_and_open_only_to_the_user()
    {
        var pipes = UniquePipes();
        using var server = Server(new SensorStore(), pipes);
        var user = WindowsIdentity.GetCurrent().User;

        foreach (var name in new[] { pipes.Values, pipes.History })
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.In);
            client.Connect(Patience);
            var security = client.GetAccessControl();

            Assert.Equal(user, security.GetOwner(typeof(SecurityIdentifier)));
            var rule = Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>());
            Assert.Equal((user, AccessControlType.Allow, PipeAccessRights.FullControl),
                (rule.IdentityReference, rule.AccessControlType, rule.PipeAccessRights));
        }
    }

    [Fact]
    public void The_client_refuses_a_pipe_owned_by_someone_else()
    {
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 52);
        var pipes = UniquePipes();
        using var server = Server(store, pipes);
        var someoneElse = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        Assert.Null(new SensorFeedClient(pipes, someoneElse).TryReadValues(Patience));
        Assert.Null(new SensorFeedClient(pipes, someoneElse).TryReadHistory(Patience));
        Assert.NotNull(new SensorFeedClient(pipes).TryReadValues(Patience));
    }

    [Fact]
    public void Bytes_a_client_sends_never_reach_the_server()
    {
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 52);
        var pipes = UniquePipes();
        var log = new ListLog();
        using var server = Server(store, pipes, log: log);
        var client = new SensorFeedClient(pipes);
        var before = client.TryReadValues(Patience);
        Assert.NotNull(before);

        // The pipes are outbound only: Windows refuses a client that asks to write, or the write itself.
        foreach (var name in new[] { pipes.Values, pipes.History })
        {
            var refused = Record.Exception(() =>
            {
                using var writer = new NamedPipeClientStream(".", name, PipeDirection.InOut);
                writer.Connect(Patience);
                writer.Write("""{"version":1,"values":{"cpu.temp":{"number":99,"stale":false}}}"""u8);
                writer.Flush();
            });
            Assert.True(refused is UnauthorizedAccessException or IOException, $"the write was taken: {refused}");
        }

        var after = client.TryReadValues(Patience);
        Assert.NotNull(after);
        Assert.Equal(before.Values, after.Values);
        Assert.Equal(52, store.Snapshot().TryGet("cpu.temp", out var temp) ? temp.Number : null);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void A_client_that_never_reads_is_dropped_at_the_write_timeout()
    {
        var store = BigStore();
        var pipes = UniquePipes();
        using var server = Server(store, pipes);

        using var stuck = new NamedPipeClientStream(".", pipes.Values, PipeDirection.In);
        stuck.Connect(Patience);

        // The render loop's side of the store is never held up by the stuck client.
        var clock = Stopwatch.StartNew();
        store.Set("cpu.temp", 52);
        Assert.True(store.Snapshot().TryGet("cpu.temp", out _));
        Assert.True(clock.Elapsed < Quickly, $"the store was held for {clock.Elapsed}");

        // The next client gets in once the server gives up on the stuck one.
        var values = new SensorFeedClient(pipes).TryReadValues(Patience);
        Assert.NotNull(values);
        Assert.Equal(52, values.Values["cpu.temp"].Number);
    }

    [Fact]
    public void Only_one_client_is_connected_at_a_time()
    {
        var pipes = UniquePipes();
        using var server = Server(BigStore(), pipes, limits: Limits(writeTimeout: Patience));
        var client = new SensorFeedClient(pipes);

        using (var stuck = new NamedPipeClientStream(".", pipes.Values, PipeDirection.In))
        {
            stuck.Connect(Patience);

            // While the server is still writing to the first client, there is no pipe for a second one.
            Assert.Null(client.TryReadValues(TimeSpan.FromMilliseconds(300)));
        }

        // Closing the first client fails the server's write, and the next client gets in.
        Assert.NotNull(client.TryReadValues(Patience));
    }

    [Fact]
    public void A_client_that_leaves_mid_message_leaves_the_server_serving()
    {
        var pipes = UniquePipes();
        using var server = Server(BigStore(), pipes, limits: Limits(writeTimeout: Patience));

        using (var leaving = new NamedPipeClientStream(".", pipes.Values, PipeDirection.In))
        {
            leaving.Connect(Patience);
            leaving.ReadExactly(new byte[100]);
        }

        Assert.NotNull(new SensorFeedClient(pipes).TryReadValues(Patience));
    }

    [Fact]
    public void At_most_five_connections_a_second_are_taken()
    {
        var time = new ManualTime(T0);
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 52);
        var pipes = UniquePipes();
        using var server = Server(store, pipes, time);
        var client = new SensorFeedClient(pipes);

        for (var i = 0; i < 5; i++)
            Assert.NotNull(client.TryReadValues(Patience));

        // The clock stands still, so a sixth connection waits for the second to be up.
        Assert.Null(client.TryReadValues(TimeSpan.FromMilliseconds(300)));

        // Once it is, the server takes connections again. (The server may not be waiting yet when the clock first
        // moves; moving it again until a read gets through doesn't depend on when it starts to.)
        Assert.True(Wait.Until(() =>
        {
            time.Advance(TimeSpan.FromSeconds(1));
            return client.TryReadValues(TimeSpan.FromMilliseconds(200)) is not null;
        }), "the server never took another connection");
    }

    [Fact]
    public void The_two_pipes_are_limited_separately()
    {
        var time = new ManualTime(T0);
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 52);
        var pipes = UniquePipes();
        using var server = Server(store, pipes, time);
        var client = new SensorFeedClient(pipes);

        for (var i = 0; i < 5; i++)
            Assert.NotNull(client.TryReadValues(Patience));

        Assert.NotNull(client.TryReadHistory(Patience));
    }

    [Fact]
    public void Dispose_stops_serving_and_can_be_repeated()
    {
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 52);
        var pipes = UniquePipes();
        var server = Server(store, pipes);
        var client = new SensorFeedClient(pipes);
        Assert.NotNull(client.TryReadValues(Patience));

        server.Dispose();
        server.Dispose();

        var clock = Stopwatch.StartNew();
        Assert.Null(client.TryReadValues(TimeSpan.FromMilliseconds(100)));
        Assert.Null(client.TryReadHistory(TimeSpan.FromMilliseconds(100)));
        Assert.True(clock.Elapsed < Quickly, $"took {clock.Elapsed}");
    }

    [Fact]
    public void Dispose_while_a_client_is_stuck_is_clean()
    {
        var pipes = UniquePipes();
        var server = Server(BigStore(), pipes, limits: Limits(writeTimeout: Patience));
        using var stuck = new NamedPipeClientStream(".", pipes.Values, PipeDirection.In);
        stuck.Connect(Patience);

        var clock = Stopwatch.StartNew();
        server.Dispose();

        Assert.True(clock.Elapsed < Quickly, $"took {clock.Elapsed}");
        Assert.Null(new SensorFeedClient(pipes).TryReadValues(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void A_pipe_name_another_process_holds_is_reported_once_and_retried()
    {
        var time = new ManualTime(T0);
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 52);
        var pipes = UniquePipes();
        var log = new ListLog();
        var held = new NamedPipeServerStream(pipes.Values, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var server = Server(store, pipes, time, log: log);
        Assert.True(Wait.Until(() => log.Entries.Count > 0));

        // Several retries, each one once the last has failed and is waiting for the next (the values pipe's retry is
        // the only timer the server has running), and still one message.
        for (var retry = 0; retry < 3; retry++)
        {
            Assert.True(Wait.Until(() => time.PendingTimers == 1), "the server isn't waiting to retry");
            time.Advance(SensorFeedLimits.Default.RetryDelay);
        }

        held.Dispose();
        var client = new SensorFeedClient(pipes);
        Assert.True(Wait.Until(() =>
        {
            time.Advance(SensorFeedLimits.Default.RetryDelay);
            return client.TryReadValues(Patience) is not null;
        }), "the server never took the pipe");
        var entry = Assert.Single(log.Entries);
        Assert.StartsWith($"warn: The theme editor's sensor feed pipe {pipes.Values} is unavailable for now, and Marquee keeps trying: ", entry);
    }

    /// Between connections, and while the rate limit holds the next one back, the pipe still exists: no other process
    /// can take the name, and a client sees a busy pipe rather than none.
    [Fact]
    public void The_pipe_name_is_held_between_connections()
    {
        var time = new ManualTime(T0);
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 52);
        var pipes = UniquePipes();
        using var server = Server(store, pipes, time);
        var client = new SensorFeedClient(pipes);
        for (var i = 0; i < 5; i++)
            Assert.NotNull(client.TryReadValues(Patience));

        // The clock stands still, so the server waits out the rate limit from here on.
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromMilliseconds(500))
        {
            Assert.True(PipeNative.Exists(pipes.Values), "the pipe's name was free");
            Thread.Sleep(10);
        }

        var squatter = Record.Exception(() => new NamedPipeServerStream(pipes.Values, PipeDirection.Out,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous).Dispose());
        Assert.True(squatter is IOException or UnauthorizedAccessException, $"another server took the name: {squatter}");
    }

    /// A second Marquee of the same user (the tray and a `marquee run`, say) never gets the pipe while the first serves
    /// it, so a client reads one store throughout rather than flapping between the two.
    [Fact]
    public void A_second_server_leaves_the_first_serving_and_says_so_once()
    {
        var (first, second) = (new SensorStore(() => T0), new SensorStore(() => T0));
        first.Set("cpu.temp", 1);
        second.Set("cpu.temp", 2);
        var pipes = UniquePipes();
        var log = new ListLog();
        using var tray = Server(first, pipes);
        using var sink = Server(second, pipes, limits: Limits() with { RetryDelay = TimeSpan.FromMilliseconds(50) }, log: log);
        var client = new SensorFeedClient(pipes);

        var read = Enumerable.Range(0, 12).Select(_ => client.TryReadValues(Patience)?.Values["cpu.temp"].Number).ToList();

        Assert.All(read, number => Assert.Equal(1, number));
        Assert.Equal(2, log.Entries.Count);
        Assert.Equal(1, log.Entries.Count(entry => entry.Contains(pipes.Values)));
        Assert.Equal(1, log.Entries.Count(entry => entry.Contains(pipes.History)));
    }

    /// So a Low-integrity process of the user's can't read the feed (UserPipe.ServerDescriptor).
    [Fact]
    public void Each_pipe_is_labelled_medium_with_no_read_up()
    {
        var pipes = UniquePipes();
        using var server = Server(new SensorStore(), pipes);

        foreach (var name in new[] { pipes.Values, pipes.History })
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.In);
            client.Connect(Patience);

            Assert.EndsWith(UserPipe.MediumLabel[2..], UserPipe.MandatoryLabel(client));
        }
    }

    /// Cutting a large history down to its cap encodes it several times, so it is built at most once a second, however
    /// many clients ask: a graph's history changes once a second at most anyway.
    [Fact]
    public void History_is_built_at_most_once_a_second()
    {
        var time = new ManualTime(T0);
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 50);
        var pipes = UniquePipes();
        using var server = Server(store, pipes, time);
        var client = new SensorFeedClient(pipes);

        var first = client.TryReadHistory(Patience);
        store.Set("cpu.temp", 51);
        var again = client.TryReadHistory(Patience);
        time.Advance(TimeSpan.FromSeconds(1));
        var later = client.TryReadHistory(Patience);

        Assert.Equal([50.0], first?.History["cpu.temp"]);
        Assert.Equal([50.0], again?.History["cpu.temp"]);
        Assert.Equal([50.0, 51], later?.History["cpu.temp"]);
    }
}
