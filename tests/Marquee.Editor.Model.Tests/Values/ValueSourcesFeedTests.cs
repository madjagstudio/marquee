using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Core.Themes.Parsing;
using Marquee.Editor.Model.Tests.TestSupport;
using Marquee.Editor.Model.Values;
using Marquee.Host.SensorFeed;

namespace Marquee.Editor.Model.Tests.Values;

/// The value sources against a real feed server on pipes of the test's own, never the tray's: values, history, and
/// what happens with no tray; and, against a fake feed, with a busy one.
public class ValueSourcesFeedTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: value, at: [0, 0], size: [100, 40], source: cpu.temp }\n" +
        "  - { type: graph, at: [0, 100], size: [200, 100], source: gpu.load }\n";

    const string WithSecondGraph = Layout +
        "  - { type: graph, at: [0, 200], size: [200, 100], source: ram.used_pct }\n";

    readonly FakeLocal _local = new();
    readonly ManualTime _time = new(T0);

    static SensorFeedPipes UniquePipes()
    {
        var id = Guid.NewGuid().ToString("N");
        return new SensorFeedPipes($"marquee-editor-test-values-{id}", $"marquee-editor-test-history-{id}");
    }

    static SensorFeedServer Serve(SensorStore store, SensorFeedPipes pipes)
    {
        var server = new SensorFeedServer(store, NullLog.Instance, TimeProvider.System, pipes);
        server.Start();
        return server;
    }

    ValueSources Sources(SensorFeedPipes pipes, string layout = Layout)
    {
        var sources = new ValueSources(new TrayFeed(new SensorFeedClient(pipes)), _local, _time);
        sources.SetTheme(LayoutParser.Parse(layout));
        return sources;
    }

    static SensorStore TrayStore()
    {
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 61.5);
        foreach (var load in new double[] { 10, 20, 30 }) store.Set("gpu.load", load);
        return store;
    }

    [Fact]
    public async Task The_trays_values_and_history_come_through()
    {
        var pipes = UniquePipes();
        using var server = Serve(TrayStore(), pipes);
        using var sources = Sources(pipes);

        await sources.PollAsync();
        var snapshot = sources.Snapshot();

        Assert.Equal(SensorFeedStatus.Ok, sources.TrayStatus);
        Assert.False(sources.TrayNotRunning);
        Assert.True(snapshot.TryGet("cpu.temp", out var temp));
        Assert.Equal(61.5, temp.Number);
        Assert.Equal(ValueSourceKind.Tray, sources.SourceOf("cpu.temp"));
        Assert.Equal([10.0, 20.0, 30.0], snapshot.History("gpu.load"));
        Assert.False(_local.IsRunning);
    }

    [Fact]
    public async Task After_the_first_history_each_reading_adds_a_point_as_the_store_does()
    {
        var pipes = UniquePipes();
        var store = TrayStore();
        using var server = Serve(store, pipes);
        using var sources = Sources(pipes);
        await sources.PollAsync();

        store.Set("gpu.load", 40);
        await sources.PollAsync();
        store.Set("gpu.load", 50);
        await sources.PollAsync();

        Assert.Equal([10.0, 20.0, 30.0, 40.0, 50.0], sources.Snapshot().History("gpu.load"));
    }

    [Fact]
    public Task A_graph_is_drawn_again_each_poll_though_its_reading_holds_steady() =>
        // The QA review's R8: the history moves on with a steady reading, so the canvas must draw it. Made off the
        // test's context, Updated is raised on the poll's own thread, as it returns.
        Task.Run(async () =>
        {
            var pipes = UniquePipes();
            using var server = Serve(TrayStore(), pipes);
            using var sources = Sources(pipes);
            await sources.PollAsync();
            var updates = 0;
            sources.Updated += (_, _) => updates++;

            await sources.PollAsync();
            await sources.PollAsync();

            Assert.Equal(2, updates);
            Assert.Equal([10.0, 20.0, 30.0, 30.0, 30.0], sources.Snapshot().History("gpu.load"));

            // Without a graph, a steady reading draws nothing.
            sources.SetTheme(LayoutParser.Parse(Layout.Replace("type: graph", "type: value")));
            await sources.PollAsync();
            updates = 0;
            await sources.PollAsync();
            Assert.Equal(0, updates);
        });

    [Fact]
    public async Task History_is_read_on_open_and_again_when_a_graph_is_added_but_not_every_second()
    {
        var pipes = UniquePipes();
        var feed = new CountingFeed(new TrayFeed(new SensorFeedClient(pipes)));
        using var server = Serve(TrayStore(), pipes);
        using var sources = new ValueSources(feed, _local, _time);
        sources.SetTheme(LayoutParser.Parse(Layout));

        await sources.PollAsync();
        await sources.PollAsync();
        Assert.Equal((2, 1), (feed.ValueReads, feed.HistoryReads));

        sources.SetTheme(LayoutParser.Parse(Layout.Replace("source: cpu.temp", "source: gpu.temp")));
        await sources.PollAsync();
        Assert.Equal(1, feed.HistoryReads);

        sources.SetTheme(LayoutParser.Parse(WithSecondGraph));
        await sources.PollAsync();
        Assert.Equal(2, feed.HistoryReads);
    }

    [Fact]
    public async Task A_tray_that_comes_back_is_asked_for_its_history_again()
    {
        var pipes = UniquePipes();
        var feed = new CountingFeed(new TrayFeed(new SensorFeedClient(pipes)));
        using var sources = new ValueSources(feed, _local, _time);
        sources.SetTheme(LayoutParser.Parse(Layout));
        using (Serve(TrayStore(), pipes))
            await sources.PollAsync();
        await sources.PollAsync();
        Assert.True(sources.TrayNotRunning);

        using var back = Serve(TrayStore(), pipes);
        await sources.PollAsync();

        Assert.Equal(2, feed.HistoryReads);
        Assert.Equal([10.0, 20.0, 30.0], sources.Snapshot().History("gpu.load"));
    }

    [Fact]
    public async Task With_no_tray_it_says_so_and_reads_locally()
    {
        using var sources = Sources(UniquePipes());
        _local.Store.Set("cpu.temp", 47);

        await sources.PollAsync();

        Assert.Equal(SensorFeedStatus.NotRunning, sources.TrayStatus);
        Assert.True(sources.TrayNotRunning);
        Assert.True(_local.IsRunning);
        Assert.Equal(ValueSourceKind.Local, sources.SourceOf("cpu.temp"));
        Assert.Equal(ValueSourceKind.Sample, sources.SourceOf("gpu.load"));
    }

    [Fact]
    public async Task When_the_tray_stops_its_values_go_and_local_readings_take_over()
    {
        var pipes = UniquePipes();
        var server = Serve(TrayStore(), pipes);
        using var sources = Sources(pipes);
        _local.Store.Set("cpu.temp", 47);
        await sources.PollAsync();
        Assert.Equal(ValueSourceKind.Tray, sources.SourceOf("cpu.temp"));

        server.Dispose();
        await sources.PollAsync();

        Assert.True(sources.TrayNotRunning);
        Assert.Equal(ValueSourceKind.Local, sources.SourceOf("cpu.temp"));
        Assert.True(_local.IsRunning);
    }

    [Fact]
    public async Task A_busy_tray_keeps_its_last_values_for_a_while()
    {
        // The feed is faked: a real tray's pipe is busy only for as long as another client holds it, which a test
        // running beside many others can't time. SensorFeedClientTests covers a held pipe reading as Busy.
        var feed = new FakeFeed { Status = SensorFeedStatus.Ok };
        feed.Set("cpu.temp", 61.5);
        using var sources = new ValueSources(feed, _local, _time);
        sources.SetTheme(LayoutParser.Parse(Layout));
        await sources.PollAsync();
        Assert.Equal(ValueSourceKind.Tray, sources.SourceOf("cpu.temp"));

        feed.Status = SensorFeedStatus.Busy;
        _time.Advance(ValueSources.TrayGrace - TimeSpan.FromSeconds(1));
        await sources.PollAsync();

        Assert.Equal(SensorFeedStatus.Busy, sources.TrayStatus);
        Assert.False(sources.TrayNotRunning);
        Assert.Equal(ValueSourceKind.Tray, sources.SourceOf("cpu.temp"));
        Assert.True(sources.Snapshot().TryGet("cpu.temp", out var temp));
        Assert.Equal(61.5, temp.Number);
        Assert.False(_local.IsRunning);

        _time.Advance(TimeSpan.FromSeconds(2));
        await sources.PollAsync();

        Assert.Equal(SensorFeedStatus.Busy, sources.TrayStatus);
        Assert.NotEqual(ValueSourceKind.Tray, sources.SourceOf("cpu.temp"));
        Assert.True(_local.IsRunning);
    }

    /// A feed that counts the reads it passes on.
    sealed class CountingFeed(ITrayFeed inner) : ITrayFeed
    {
        public int ValueReads { get; private set; }
        public int HistoryReads { get; private set; }

        public Task<SensorFeedResult<SensorFeedValues>> ReadValuesAsync(TimeSpan timeout, CancellationToken cancel = default)
        {
            ValueReads++;
            return inner.ReadValuesAsync(timeout, cancel);
        }

        public Task<SensorFeedResult<SensorFeedHistory>> ReadHistoryAsync(TimeSpan timeout, CancellationToken cancel = default)
        {
            HistoryReads++;
            return inner.ReadHistoryAsync(timeout, cancel);
        }
    }
}
