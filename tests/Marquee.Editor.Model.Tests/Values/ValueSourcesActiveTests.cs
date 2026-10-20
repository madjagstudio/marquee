using Marquee.Core.Themes.Parsing;
using Marquee.Editor.Model.Tests.TestSupport;
using Marquee.Editor.Model.Values;
using Marquee.Host.SensorFeed;

namespace Marquee.Editor.Model.Tests.Values;

/// The poll: once a second while the window is shown, off the caller's thread, and not at all while it is minimised.
public class ValueSourcesActiveTests
{
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    readonly FakeFeed _feed = new() { Status = SensorFeedStatus.Ok };
    readonly FakeLocal _local = new();
    readonly ManualTimers _time = new();

    ValueSources Sources()
    {
        var sources = new ValueSources(_feed, _local, _time) { DisposeWait = Timeout.InfiniteTimeSpan };
        sources.SetTheme(LayoutParser.Parse(
            "canvas: { orientation: landscape }\nwidgets:\n  - { type: value, at: [0, 0], size: [10, 10], source: cpu.temp }\n"));
        return sources;
    }

    void WaitForReads(int reads) =>
        Assert.True(SpinWait.SpinUntil(() => _feed.ValueReads >= reads, Patience), $"{_feed.ValueReads} reads, wanted {reads}");

    /// The poll's timer is waiting again, so the next Advance reaches it.
    void WaitForTimer() => Assert.True(SpinWait.SpinUntil(() => _time.Pending == 1, Patience), "the poll never waited");

    [Fact]
    public void Nothing_is_read_until_it_is_made_active()
    {
        using var sources = Sources();

        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.False(sources.IsActive);
        Assert.Equal(0, _feed.ValueReads);
        Assert.Equal(0, _time.Pending);
    }

    [Fact]
    public void Active_it_reads_at_once_then_once_a_second()
    {
        using var sources = Sources();

        sources.SetActive(true);
        WaitForReads(1);
        WaitForTimer();
        _time.Advance(ValueSources.PollInterval);
        WaitForReads(2);
        WaitForTimer();
        _time.Advance(ValueSources.PollInterval);
        WaitForReads(3);

        Assert.True(sources.IsActive);
    }

    [Fact]
    public void Inactive_it_stops_reading_and_stops_its_local_readings()
    {
        using var sources = Sources();
        _feed.Status = SensorFeedStatus.NotRunning;
        sources.SetActive(true);
        WaitForReads(1);
        WaitForTimer();
        Assert.True(_local.IsRunning);

        sources.SetActive(false);
        Assert.True(SpinWait.SpinUntil(() => _time.Pending == 0, Patience), "the poll's timer is still waiting");
        _time.Advance(TimeSpan.FromSeconds(10));
        Thread.Sleep(100);

        Assert.Equal(1, _feed.ValueReads);
        Assert.False(_local.IsRunning);
        Assert.False(sources.IsActive);
    }

    [Fact]
    public void Active_again_it_reads_again()
    {
        using var sources = Sources();
        sources.SetActive(true);
        WaitForReads(1);
        sources.SetActive(false);

        sources.SetActive(true);

        WaitForReads(2);
    }

    /// A clock (`{now:HH:mm:ss}`) changes every second with no reading changing, so a theme that reads `now` is redrawn
    /// at each poll, unless the clock is pinned.
    [Fact]
    public Task A_theme_that_reads_the_clock_is_updated_at_every_poll_unless_it_is_pinned() =>
        // Made off the test's context, Updated is raised on the poll's own thread, as it returns.
        Task.Run(async () =>
        {
            using var sources = new ValueSources(_feed, _local, _time);
            sources.SetTheme(LayoutParser.Parse(
                "canvas: { orientation: landscape }\nwidgets:\n  - { type: text, at: [0, 0], size: [100, 20], text: \"{now:HH:mm:ss}\" }\n"));
            var updated = 0;
            sources.Updated += (_, _) => updated++;

            for (var i = 0; i < 3; i++) await sources.PollAsync();
            Assert.Equal(3, updated);

            sources.TestValues.PinNow(_time.GetUtcNow());
            updated = 0;
            for (var i = 0; i < 3; i++) await sources.PollAsync();
            Assert.Equal(0, updated);
        });

    [Fact]
    public void A_read_that_changes_a_value_the_theme_reads_raises_updated()
    {
        using var sources = Sources();
        using var updated = new SemaphoreSlim(0);
        // SetTheme's own Updated is posted to the test's context and may arrive after this subscribes: only one raised
        // once the tray has been read counts. Unsubscribed before the semaphore goes, as a post may still be on its way.
        EventHandler onUpdated = (_, _) =>
        {
            if (sources.TrayStatus is not null) updated.Release();
        };
        sources.Updated += onUpdated;
        _feed.Set("cpu.temp", 50);

        sources.SetActive(true);

        Assert.True(updated.Wait(Patience));
        sources.Updated -= onUpdated;
        Assert.Equal(_time.GetUtcNow(), sources.ChangedAt);
    }

    /// With the fake feed, so a busy read under load can't restart the linger (a real pipe's read has a 500 ms bound).
    [Fact]
    public async Task Local_readings_stop_once_the_tray_has_been_back_a_while()
    {
        using var sources = Sources();
        _feed.Status = SensorFeedStatus.NotRunning;
        await sources.PollAsync();
        Assert.True(_local.IsRunning);

        _feed.Status = SensorFeedStatus.Ok;
        _feed.Set("cpu.temp", 50);
        await sources.PollAsync();
        Assert.True(_local.IsRunning);

        _time.Advance(ValueSources.LocalLinger - TimeSpan.FromSeconds(1));
        await sources.PollAsync();
        Assert.True(_local.IsRunning);

        _time.Advance(TimeSpan.FromSeconds(1));
        await sources.PollAsync();
        Assert.False(_local.IsRunning);
        Assert.Equal(ValueSourceKind.Tray, sources.SourceOf("cpu.temp"));
    }

    [Fact]
    public void Dispose_stops_the_poll_and_disposes_the_local_readings()
    {
        var sources = Sources();
        sources.SetActive(true);
        WaitForReads(1);

        sources.Dispose();

        Assert.True(SpinWait.SpinUntil(() => _time.Pending == 0, Patience));
        Assert.True(_local.Disposed);
    }

    [Fact]
    public void Dispose_waits_for_local_readings_slow_to_stop_only_so_long()
    {
        using var release = new ManualResetEventSlim();
        var slow = new SlowLocal(release);
        // No wait at all, so the bound decides, not how quickly this machine runs: a provider stuck in a driver call
        // (up to 3 s each) doesn't hold the closing window, and finishes stopping on a thread of its own.
        var sources = new ValueSources(_feed, slow, _time) { DisposeWait = TimeSpan.Zero };

        sources.Dispose();

        Assert.False(slow.Disposed);
        release.Set();
        Assert.True(SpinWait.SpinUntil(() => slow.Disposed, Patience));
    }

    [Fact]
    public void Dispose_stops_the_local_readings_on_a_thread_of_its_own()
    {
        var local = new SlowLocal(new ManualResetEventSlim(initialState: true));
        var sources = new ValueSources(_feed, local, _time) { DisposeWait = Timeout.InfiniteTimeSpan };

        sources.Dispose();

        Assert.True(local.Disposed);
        Assert.False(local.DisposedOnPool);
    }

    /// Local readings whose stop takes until the test lets it go.
    sealed class SlowLocal(ManualResetEventSlim release) : ILocalReadings
    {
        volatile bool _disposed;

        public bool Disposed => _disposed;

        public bool DisposedOnPool { get; private set; }

        public bool IsRunning => false;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public Marquee.Core.Sensors.SensorSnapshot Snapshot() => new FakeLocal().Snapshot();

        public void Dispose()
        {
            release.Wait(TimeSpan.FromSeconds(20));
            DisposedOnPool = Thread.CurrentThread.IsThreadPoolThread;
            _disposed = true;
        }
    }
}
