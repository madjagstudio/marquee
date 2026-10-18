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
    public void A_string_write_clears_the_numeric_history()
    {
        var store = new SensorStore(() => T0);
        store.Set("gpu.temp", 70);
        store.Set("gpu.temp", 71);

        store.Set("gpu.temp", "N/A");

        var snapshot = store.Snapshot();
        Assert.Empty(snapshot.History("gpu.temp"));
        Assert.True(snapshot.TryGet("gpu.temp", out var value));
        Assert.Equal("N/A", value.Text);
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

    [Fact]
    public void RemoveStale_removes_a_key_once_it_has_been_stale_for_the_full_grace()
    {
        var now = T0;
        var store = new SensorStore(() => now);
        store.Set("custom.build", "ok", TimeSpan.FromSeconds(30));
        var grace = TimeSpan.FromMinutes(10);

        now = T0.AddSeconds(30) + grace - TimeSpan.FromTicks(1);
        Assert.Empty(store.RemoveStale("custom.", grace));
        Assert.Equal(["custom.build"], store.Snapshot().Keys);

        now = T0.AddSeconds(30) + grace;
        Assert.Equal(["custom.build"], store.RemoveStale("custom.", grace));
        Assert.Empty(store.Snapshot().Keys);
        Assert.Empty(store.Snapshot().History("custom.build"));
        Assert.Empty(store.RemoveStale("custom.", grace));
    }

    [Fact]
    public void RemoveStale_keeps_other_prefixes_and_keys_without_a_ttl()
    {
        var now = T0;
        var store = new SensorStore(() => now);
        store.Set("custom.queue", 3, TimeSpan.FromSeconds(1));
        store.Set("custom.build", "ok", TimeSpan.FromSeconds(1));
        store.Set("custom.forever", 1);
        store.Set("customer.count", 2, TimeSpan.FromSeconds(1));
        store.Set("cpu.temp", 60, TimeSpan.FromSeconds(1));
        now = T0.AddDays(30);

        var removed = store.RemoveStale("custom.", TimeSpan.FromMinutes(10));

        Assert.Equal(["custom.build", "custom.queue"], removed.Order(StringComparer.Ordinal));
        Assert.Equal(["cpu.temp", "custom.forever", "customer.count"], store.Snapshot().Keys);
    }

    [Fact]
    public void RemoveStale_keeps_a_key_rewritten_after_it_expired()
    {
        var now = T0;
        var store = new SensorStore(() => now);
        store.Set("custom.build", "failing", TimeSpan.FromSeconds(1));
        now = T0.AddMinutes(5);
        store.Set("custom.build", "ok", TimeSpan.FromSeconds(1));

        // Eleven minutes after the first write expired, but only six after the rewrite did.
        now = T0.AddMinutes(11);

        Assert.Empty(store.RemoveStale("custom.", TimeSpan.FromMinutes(10)));
        Assert.Equal(["custom.build"], store.Snapshot().Keys);
    }

    [Fact]
    public void Keys_lists_the_keys_with_a_prefix_stale_or_not()
    {
        var now = T0;
        var store = new SensorStore(() => now);
        store.Set("custom.build", "ok", TimeSpan.FromSeconds(1));
        store.Set("custom.queue", 3);
        store.Set("customer.count", 2);
        store.Set("cpu.temp", 60);
        now = T0.AddDays(1);

        Assert.Equal(["custom.build", "custom.queue"], store.Keys("custom.").Order(StringComparer.Ordinal));
        Assert.Empty(store.Keys("gpu."));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_writes_are_ignored(double unreadable)
    {
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 60);

        store.Set("cpu.temp", unreadable);
        store.Set("gpu.temp", unreadable);

        var snapshot = store.Snapshot();
        Assert.True(snapshot.TryGet("cpu.temp", out var value));
        Assert.Equal(60, value.Number);
        Assert.Equal(new double[] { 60 }, snapshot.History("cpu.temp"));
        Assert.False(snapshot.TryGet("gpu.temp", out _));
    }
}
