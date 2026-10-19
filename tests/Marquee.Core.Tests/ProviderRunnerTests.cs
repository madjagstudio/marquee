using Marquee.Core.Sensors;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

public class ProviderRunnerTests
{
    sealed class FakeProvider(string name, Action<int, SensorStore> poll, Action? onDispose = null) : ISensorProvider
    {
        int _polls;

        public string Name => name;
        public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(20);
        public int Polls => Volatile.Read(ref _polls);
        public bool Disposed { get; private set; }

        public void Poll(SensorStore store) => poll(Interlocked.Increment(ref _polls), store);

        public void Dispose()
        {
            Disposed = true;
            onDispose?.Invoke();
        }
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

    [Fact]
    public void Stop_ends_and_disposes_one_provider_and_leaves_the_others_running()
    {
        var stopped = new FakeProvider("stopped", (_, _) => { });
        var kept = new FakeProvider("kept", (_, _) => { });
        using var runner = new ProviderRunner(new SensorStore(), new ListLog());
        runner.Start(stopped);
        runner.Start(kept);
        Assert.True(Wait.Until(() => stopped.Polls >= 1 && kept.Polls >= 1));

        runner.Stop(stopped);
        var polls = stopped.Polls;
        var keptPolls = kept.Polls;

        Assert.True(stopped.Disposed);
        Assert.True(Wait.Until(() => kept.Polls > keptPolls + 2), "The other provider stopped too.");
        Assert.Equal(polls, stopped.Polls);
        Assert.False(kept.Disposed);
        runner.Stop(stopped); // a provider it no longer runs: nothing happens
    }

    [Fact]
    public void Dispose_twice_is_safe()
    {
        var provider = new FakeProvider("fake", (_, _) => { });
        var runner = new ProviderRunner(new SensorStore(), new ListLog());
        runner.Start(provider);
        Assert.True(Wait.Until(() => provider.Polls >= 1));

        runner.Dispose();
        runner.Dispose();

        Assert.True(provider.Disposed);
    }

    [Fact]
    public void Start_after_dispose_throws()
    {
        var provider = new FakeProvider("fake", (_, _) => { });
        var runner = new ProviderRunner(new SensorStore(), new ListLog());
        runner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runner.Start(provider));
        Thread.Sleep(50);

        Assert.Equal(0, provider.Polls);
    }

    [Fact]
    public void One_provider_failing_to_shut_down_does_not_stop_the_others()
    {
        var log = new ListLog();
        var first = new FakeProvider("first", (_, _) => { }, () => throw new IOException("stuck"));
        var second = new FakeProvider("second", (_, _) => { });
        var runner = new ProviderRunner(new SensorStore(), log);
        runner.Start(first);
        runner.Start(second);
        Assert.True(Wait.Until(() => first.Polls >= 1 && second.Polls >= 1));

        runner.Dispose();

        Assert.True(second.Disposed);
        Assert.Contains("error: first failed to shut down: stuck", log.Entries);
    }

    [Fact]
    public async Task Dispose_does_not_wait_for_a_provider_that_will_not_stop_says_so_and_disposes_it_when_its_poll_returns()
    {
        var release = new ManualResetEventSlim(); // not disposed: the provider thread may still be waking from it
        var log = new ListLog();
        var stuck = new FakeProvider("stuck", (_, _) => release.Wait());
        var runner = new ProviderRunner(new SensorStore(), log);
        runner.Start(stuck);
        Assert.True(Wait.Until(() => stuck.Polls >= 1));

        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var disposing = Task.Run(runner.Dispose);

            var returned = await Task.WhenAny(disposing, Task.Delay(TimeSpan.FromSeconds(5))) == disposing;

            Assert.True(returned, "Dispose did not return");
            Assert.InRange(timer.Elapsed, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5));
            Assert.False(stuck.Disposed);
            Assert.Equal(["warn: stuck did not stop within 3 s; it will be shut down when its current poll returns."], log.Entries);
        }
        finally
        {
            release.Set();
        }

        Assert.True(Wait.Until(() => stuck.Disposed), "The provider was never disposed once its poll returned");
    }

    [Fact]
    public void Stop_disposes_a_provider_stuck_in_a_poll_once_the_poll_returns()
    {
        // A settings change can stop one provider in a process that runs for days, so a stuck one can't just be left
        // for the process exit: its driver handle or child process would stay open until then.
        var release = new ManualResetEventSlim(); // not disposed: the provider thread may still be waking from it
        var log = new ListLog();
        var stuck = new FakeProvider("stuck", (_, _) => release.Wait());
        using var runner = new ProviderRunner(new SensorStore(), log) { StopTimeout = TimeSpan.FromMilliseconds(100) };
        runner.Start(stuck);
        Assert.True(Wait.Until(() => stuck.Polls >= 1));

        try
        {
            runner.Stop(stuck);

            Assert.False(stuck.Disposed); // never under a running poll
            Assert.Contains("stuck did not stop", Assert.Single(log.Entries));
        }
        finally
        {
            release.Set();
        }

        Assert.True(Wait.Until(() => stuck.Disposed), "The provider was never disposed once its poll returned");
        Assert.Equal(1, stuck.Polls);
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
