using Marquee.Core.Sensors;

namespace Marquee.Core.Tests;

public class SensorStoreTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Snapshot_returns_numbers_and_strings()
    {
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 72.5);
        store.Set("cpu.name", "Ryzen 9");

        var snapshot = store.Snapshot();

        Assert.True(snapshot.TryGet("cpu.temp", out var temp));
        Assert.Equal(72.5, temp.Number);
        Assert.True(snapshot.TryGet("cpu.name", out var name));
        Assert.Equal("Ryzen 9", name.Text);
        Assert.Null(name.Number);
        Assert.Equal(T0, snapshot.Now);
    }

    [Fact]
    public void Missing_key_is_not_found() =>
        Assert.False(new SensorStore(() => T0).Snapshot().TryGet("gpu.temp", out _));

    [Fact]
    public void Value_goes_stale_when_its_ttl_passes()
    {
        var now = T0;
        var store = new SensorStore(() => now);
        store.Set("cpu.temp", 60, TimeSpan.FromSeconds(5));

        now = T0.AddSeconds(4.9);
        Assert.True(store.Snapshot().TryGet("cpu.temp", out _));

        now = T0.AddSeconds(5);
        Assert.False(store.Snapshot().TryGet("cpu.temp", out _));
    }

    [Fact]
    public void Value_without_ttl_never_goes_stale()
    {
        var now = T0;
        var store = new SensorStore(() => now);
        store.Set("cpu.temp", 60);

        now = T0.AddDays(30);

        Assert.True(store.Snapshot().TryGet("cpu.temp", out _));
    }

    [Fact]
    public void Rewriting_a_stale_key_makes_it_fresh_again()
    {
        var now = T0;
        var store = new SensorStore(() => now);
        store.Set("cpu.temp", 60, TimeSpan.FromSeconds(1));
        now = T0.AddSeconds(10);

        store.Set("cpu.temp", 61, TimeSpan.FromSeconds(1));

        Assert.True(store.Snapshot().TryGet("cpu.temp", out var value));
        Assert.Equal(61, value.Number);
    }

    [Fact]
    public void History_keeps_the_last_n_numbers_oldest_first()
    {
        var store = new SensorStore(() => T0, historyLength: 3);
        foreach (var v in new double[] { 1, 2, 3, 4, 5 })
            store.Set("gpu.load", v);

        Assert.Equal(new double[] { 3, 4, 5 }, store.Snapshot().History("gpu.load"));
    }

    [Fact]
    public void Strings_and_unknown_keys_have_no_history()
    {
        var store = new SensorStore(() => T0);
        store.Set("cpu.name", "Ryzen 9");

        var snapshot = store.Snapshot();

        Assert.Empty(snapshot.History("cpu.name"));
        Assert.Empty(snapshot.History("nope"));
    }

    [Fact]
    public void Snapshot_does_not_see_later_writes()
    {
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 60);
        var snapshot = store.Snapshot();

        store.Set("cpu.temp", 99);
        store.Set("gpu.temp", 50);

        Assert.True(snapshot.TryGet("cpu.temp", out var temp));
        Assert.Equal(60, temp.Number);
        Assert.Equal(new double[] { 60 }, snapshot.History("cpu.temp"));
        Assert.Equal(["cpu.temp"], snapshot.Keys);
    }

    [Fact]
    public void Concurrent_writers_and_readers_do_not_corrupt_the_store()
    {
        var store = new SensorStore();

        Parallel.For(0, 4, writer =>
        {
            for (var i = 0; i < 2000; i++)
            {
                store.Set($"key.{writer}", i);
                _ = store.Snapshot().History($"key.{(writer + 1) % 4}");
            }
        });

        var snapshot = store.Snapshot();
        for (var writer = 0; writer < 4; writer++)
        {
            Assert.True(snapshot.TryGet($"key.{writer}", out var value));
            Assert.Equal(1999, value.Number);
            Assert.Equal(SensorStore.DefaultHistoryLength, snapshot.History($"key.{writer}").Count);
        }
    }
}
