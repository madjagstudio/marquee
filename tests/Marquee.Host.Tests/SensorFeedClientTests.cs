using System.Diagnostics;
using System.IO.Pipes;
using Marquee.Core.Pipes;
using Marquee.Core.Sensors;
using Marquee.Host.SensorFeed;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

/// The client as the theme editor uses it: from the UI thread, with a result that says why there is no message.
public class SensorFeedClientTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    static SensorFeedPipes UniquePipes()
    {
        var id = Guid.NewGuid().ToString("N");
        return new SensorFeedPipes($"marquee-test-values-{id}", $"marquee-test-history-{id}");
    }

    static SensorFeedServer Server(SensorStore store, SensorFeedPipes pipes)
    {
        var server = new SensorFeedServer(store, new ListLog(), TimeProvider.System, pipes,
            SensorFeedLimits.Default with { WriteTimeout = TimeSpan.FromMilliseconds(300) });
        server.Start();
        return server;
    }

    /// No message, and why.
    static void Failed<T>(SensorFeedStatus status, SensorFeedResult<T> read) where T : class =>
        Assert.Equal((status, (T?)null), (read.Status, read.Message));

    static SensorStore Store()
    {
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 52.5);
        return store;
    }

    [Fact]
    public void A_synchronous_read_on_a_ui_thread_does_not_deadlock()
    {
        var pipes = UniquePipes();
        using var server = Server(Store(), pipes);
        var client = new SensorFeedClient(pipes);
        var context = new QueueOnlyContext();
        SensorFeedValues? values = null;
        SensorFeedHistory? history = null;

        var finished = context.Run(() =>
        {
            values = client.TryReadValues(TimeSpan.FromSeconds(5));
            history = client.TryReadHistory(TimeSpan.FromSeconds(5));
        }, Patience);

        Assert.True(finished, $"the read deadlocked ({context.Posted} continuations posted to the UI thread)");
        Assert.Equal(52.5, values?.Values["cpu.temp"].Number);
        Assert.Equal([52.5], history?.History["cpu.temp"]);
    }

    /// The editor should await the asynchronous reads, but blocking on one mustn't deadlock either.
    [Fact]
    public void Blocking_on_an_asynchronous_read_on_a_ui_thread_does_not_deadlock()
    {
        var pipes = UniquePipes();
        using var server = Server(Store(), pipes);
        var client = new SensorFeedClient(pipes);
        var context = new QueueOnlyContext();
        SensorFeedResult<SensorFeedValues>? read = null;

        var finished = context.Run(() => read = client.ReadValuesAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(), Patience);

        Assert.True(finished, $"the read deadlocked ({context.Posted} continuations posted to the UI thread)");
        Assert.Equal(SensorFeedStatus.Ok, read?.Status);
    }

    [Fact]
    public async Task An_asynchronous_read_returns_the_message()
    {
        var pipes = UniquePipes();
        using var server = Server(Store(), pipes);
        var client = new SensorFeedClient(pipes);

        var values = await client.ReadValuesAsync(Patience);
        var history = await client.ReadHistoryAsync(Patience);

        Assert.Equal(SensorFeedStatus.Ok, values.Status);
        Assert.Equal(52.5, values.Message?.Values["cpu.temp"].Number);
        Assert.Equal(SensorFeedStatus.Ok, history.Status);
        Assert.Equal([52.5], history.Message?.History["cpu.temp"]);
    }

    [Fact]
    public async Task With_no_server_the_read_says_not_running()
    {
        var client = new SensorFeedClient(UniquePipes());

        Failed(SensorFeedStatus.NotRunning, await client.ReadValuesAsync(TimeSpan.FromMilliseconds(200)));
        Failed(SensorFeedStatus.NotRunning, await client.ReadHistoryAsync(TimeSpan.FromMilliseconds(200)));
    }

    /// A 1 Hz poll with no tray mustn't stall for its whole timeout every second.
    [Fact]
    public async Task With_no_server_the_read_says_so_without_waiting_out_its_timeout()
    {
        var client = new SensorFeedClient(UniquePipes());
        var clock = Stopwatch.StartNew();

        Failed(SensorFeedStatus.NotRunning, await client.ReadValuesAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(client.TryReadHistory(TimeSpan.FromSeconds(10)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task While_another_client_is_served_the_read_says_busy()
    {
        var pipes = UniquePipes();
        using var server = new SensorFeedServer(BigStore(), new ListLog(), TimeProvider.System, pipes,
            SensorFeedLimits.Default with { WriteTimeout = Patience });
        server.Start();
        using var stuck = new NamedPipeClientStream(".", pipes.Values, PipeDirection.In);
        stuck.Connect(Patience);

        var read = await new SensorFeedClient(pipes).ReadValuesAsync(TimeSpan.FromMilliseconds(300));

        Failed(SensorFeedStatus.Busy, read);
    }

    [Fact]
    public async Task Cancelling_a_read_throws()
    {
        var pipes = UniquePipes();
        using var server = new SensorFeedServer(BigStore(), new ListLog(), TimeProvider.System, pipes,
            SensorFeedLimits.Default with { WriteTimeout = Patience });
        server.Start();
        using var stuck = new NamedPipeClientStream(".", pipes.Values, PipeDirection.In);
        stuck.Connect(Patience);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SensorFeedClient(pipes).ReadValuesAsync(Patience, cancel.Token));
    }

    [Fact]
    public async Task A_server_that_never_writes_times_out()
    {
        var pipes = UniquePipes();
        using var silent = UserPipe.CreateServer(pipes.Values, PipeDirection.Out, 1);
        var accepted = silent.WaitForConnectionAsync();

        var read = await new SensorFeedClient(pipes).ReadValuesAsync(TimeSpan.FromMilliseconds(300));

        Failed(SensorFeedStatus.TimedOut, read);
        await accepted;
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 }, true)] // a length of 0
    [InlineData(new byte[] { 1, 0, 16, 0 }, true)] // 1 MB and a byte, over the values cap
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, true)]
    [InlineData(new byte[] { 100, 0, 0, 0, (byte)'{', (byte)'"' }, true)] // the server leaves before the rest
    [InlineData(new byte[] { 2, 0 }, true)] // the length itself cut short
    [InlineData(new byte[] { 2, 0, 0, 0, (byte)'{', (byte)'}' }, true)] // JSON, but not a message
    [InlineData(new byte[] { 3, 0, 0, 0, 0xC3, 0x28, 0x41 }, true)] // not UTF-8
    public async Task A_bad_message_is_bad_data(byte[] bytes, bool hangUp) =>
        Failed(SensorFeedStatus.BadData, await ReadFrom(bytes, hangUp, Patience));

    [Fact]
    public async Task A_message_that_stalls_partway_times_out() =>
        Failed(SensorFeedStatus.TimedOut, await ReadFrom([100, 0, 0, 0, (byte)'{', (byte)'"'], hangUp: false, TimeSpan.FromMilliseconds(300)));

    /// What a client reads from a server of the test's own that sends `bytes`, then hangs up or stays connected.
    static async Task<SensorFeedResult<SensorFeedValues>> ReadFrom(byte[] bytes, bool hangUp, TimeSpan timeout)
    {
        var pipes = UniquePipes();
        var server = UserPipe.CreateServer(pipes.Values, PipeDirection.Out, 1);
        var serving = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            await server.WriteAsync(bytes);
            if (hangUp) server.Disconnect();
        });

        try
        {
            return await new SensorFeedClient(pipes).ReadValuesAsync(timeout);
        }
        finally
        {
            await server.DisposeAsync();
            await Record.ExceptionAsync(() => serving);
        }
    }

    /// Enough text that the message can't sit whole in the pipe's buffers: a write blocks until the client reads it.
    static SensorStore BigStore()
    {
        var store = new SensorStore(() => T0);
        for (var i = 0; i < 1000; i++) store.Set($"custom.text{i:D4}", new string('x', 250));
        return store;
    }
}
