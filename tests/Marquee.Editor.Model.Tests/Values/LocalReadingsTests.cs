using Marquee.Core.Sensors;
using Marquee.Editor.Model.Values;

namespace Marquee.Editor.Model.Tests.Values;

/// The editor's own readings, run as the tray runs its providers. These use stand-in providers: never real sensors.
public class LocalReadingsTests
{
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    sealed class Provider(string key) : ISensorProvider
    {
        int _polls;

        public string Name => key;
        public TimeSpan Interval => TimeSpan.FromMilliseconds(20);
        public int Polls => Volatile.Read(ref _polls);
        public bool Disposed { get; private set; }

        public void Poll(SensorStore store) => store.Set(key, Interlocked.Increment(ref _polls));

        public void Dispose() => Disposed = true;
    }

    sealed class Builds
    {
        public List<Provider> Made { get; } = [];

        public Func<ISensorProvider>[] Providers => [() => Make("gpu.load"), () => Make("claude.sessions.total")];

        Provider Make(string key)
        {
            var provider = new Provider(key);
            lock (Made) Made.Add(provider);
            return provider;
        }
    }

    [Fact]
    public void Nothing_runs_until_started()
    {
        var builds = new Builds();
        using var local = new LocalReadings(builds.Providers);

        Assert.False(local.IsRunning);
        Assert.Empty(builds.Made);
        Assert.Empty(local.Snapshot().Keys);
    }

    [Fact]
    public void Started_each_provider_writes_its_keys()
    {
        var builds = new Builds();
        using var local = new LocalReadings(builds.Providers);

        local.Start();

        Assert.True(local.IsRunning);
        Assert.True(SpinWait.SpinUntil(() => local.Snapshot().Keys.Count() == 2, Patience));
        Assert.True(local.Snapshot().TryGet("gpu.load", out _));
    }

    [Fact]
    public void Stopping_disposes_the_providers_and_keeps_what_they_read()
    {
        var builds = new Builds();
        using var local = new LocalReadings(builds.Providers);
        local.Start();
        Assert.True(SpinWait.SpinUntil(() => local.Snapshot().Keys.Count() == 2, Patience));

        local.Stop();

        Assert.False(local.IsRunning);
        Assert.True(SpinWait.SpinUntil(() => builds.Made.All(provider => provider.Disposed), Patience));
        var polls = builds.Made.Sum(provider => provider.Polls);
        Thread.Sleep(100);
        Assert.Equal(polls, builds.Made.Sum(provider => provider.Polls));
        Assert.True(local.Snapshot().TryGetLast("gpu.load", out _, out _));
    }

    [Fact]
    public void Started_again_it_builds_new_providers()
    {
        var builds = new Builds();
        using var local = new LocalReadings(builds.Providers);
        local.Start();
        local.Start();
        local.Stop();

        local.Start();

        Assert.Equal(4, builds.Made.Count);
    }

    [Fact]
    public void Dispose_stops_them_and_a_later_start_does_nothing()
    {
        var builds = new Builds();
        var local = new LocalReadings(builds.Providers);
        local.Start();

        local.Dispose();
        local.Start();

        Assert.False(local.IsRunning);
        Assert.Equal(2, builds.Made.Count);
        Assert.All(builds.Made, provider => Assert.True(provider.Disposed));
    }

    [Fact]
    public void A_provider_that_cannot_be_built_leaves_the_rest_running()
    {
        var builds = new Builds();
        using var local = new LocalReadings([() => throw new InvalidOperationException("no sensors here"), .. builds.Providers]);

        local.Start();

        Assert.True(local.IsRunning);
        Assert.True(SpinWait.SpinUntil(() => local.Snapshot().Keys.Count() == 2, Patience));
    }
}
