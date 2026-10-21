using Marquee.Core.Sensors;

namespace Marquee.Core.Tests;

public class SensorSnapshotTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryGetLast_gives_a_fresh_value()
    {
        var store = new SensorStore(() => T0);
        store.Set("cpu.temp", 52, TimeSpan.FromSeconds(5));

        Assert.True(store.Snapshot().TryGetLast("cpu.temp", out var value, out var stale));

        Assert.Equal((52.0, false), (value.Number, stale));
    }

    [Fact]
    public void TryGetLast_gives_an_expired_value_as_stale()
    {
        var now = T0;
        var store = new SensorStore(() => now);
        store.Set("cpu.name", "Ryzen 9", TimeSpan.FromSeconds(5));
        now = T0.AddSeconds(5);

        Assert.True(store.Snapshot().TryGetLast("cpu.name", out var value, out var stale));

        Assert.Equal(("Ryzen 9", true), (value.Text, stale));
        Assert.False(store.Snapshot().TryGet("cpu.name", out _));
    }

    [Fact]
    public void TryGetLast_finds_no_unknown_key() =>
        Assert.False(new SensorStore(() => T0).Snapshot().TryGetLast("gpu.temp", out _, out _));
}
