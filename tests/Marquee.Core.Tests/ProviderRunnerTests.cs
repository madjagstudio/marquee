using Marquee.Core.Sensors;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

public class ProviderRunnerTests
{
    sealed class FakeProvider(string name, Action<int, SensorStore> poll) : ISensorProvider
    {
        int _polls;

        public string Name => name;
        public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(20);
        public int Polls => Volatile.Read(ref _polls);
        public bool Disposed { get; private set; }

        public void Poll(SensorStore store) => poll(Interlocked.Increment(ref _polls), store);

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Polls_each_provider_on_its_interval()
    {
        var store = new SensorStore();
        var provider = new FakeProvider("fake", (n, s) => s.Set("fake.count", n));
        using (var runner = new ProviderRunner(store, new ListLog()))
        {
            runner.Start(provider);
            Assert.True(Wait.Until(() => provider.Polls >= 3));
        }

        Assert.True(store.Snapshot().TryGet("fake.count", out var count));
        Assert.True(count.Number >= 3);
    }

    [Fact]
    public void A_failing_provider_is_retried_and_its_recovery_logged()
    {
        var store = new SensorStore();
        var log = new ListLog();
        var provider = new FakeProvider("Hardware sensors", (n, s) =>
        {
            if (n <= 2) throw new InvalidOperationException("Sensor glitch.");
            s.Set("cpu.temp", 60);
        });

        using (var runner = new ProviderRunner(store, log, firstRetry: TimeSpan.FromMilliseconds(10)))
        {
            runner.Start(provider);
            Assert.True(Wait.Until(() => store.Snapshot().TryGet("cpu.temp", out _)));
        }

        Assert.Equal(
            [
                "error: Hardware sensors failed: Sensor glitch. Its values will go stale until it recovers.",
                "info: Hardware sensors is working again.",
            ],
            log.Entries);
    }

    [Fact]
    public void One_broken_provider_does_not_stop_another()
    {
        var store = new SensorStore();
        var broken = new FakeProvider("broken", (_, _) => throw new IOException("gone"));
        var working = new FakeProvider("working", (n, s) => s.Set("ok", n));

        using (var runner = new ProviderRunner(store, new ListLog(), TimeSpan.FromMilliseconds(10)))
        {
            runner.Start(broken);
            runner.Start(working);
            Assert.True(Wait.Until(() => working.Polls >= 3));
        }

        Assert.True(store.Snapshot().TryGet("ok", out _));
    }

    [Fact]
    public void Dispose_stops_polling_and_disposes_providers()
    {
        var provider = new FakeProvider("fake", (_, _) => { });
        var runner = new ProviderRunner(new SensorStore(), new ListLog());
        runner.Start(provider);
        Assert.True(Wait.Until(() => provider.Polls >= 1));

        runner.Dispose();
        var polls = provider.Polls;
        Thread.Sleep(100);

        Assert.True(provider.Disposed);
        Assert.Equal(polls, provider.Polls);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(1000, 60)]
    public void Backoff_doubles_up_to_a_minute(int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), ProviderRunner.Backoff(TimeSpan.FromSeconds(1), failures));
}
