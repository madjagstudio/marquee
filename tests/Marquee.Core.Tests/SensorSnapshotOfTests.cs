using Marquee.Core.Sensors;

namespace Marquee.Core.Tests;

/// A snapshot made from entries rather than a store, as the theme editor makes its own.
public class SensorSnapshotOfTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entries_read_as_a_stores_do()
    {
        var entries = new Dictionary<string, SensorEntry>
        {
            ["cpu.temp"] = new(SensorValue.Of(52), T0, null, [50, 52]),
            ["gpu.temp"] = new(SensorValue.Of(58), T0, T0, [58]),
        };

        var snapshot = SensorSnapshot.Of(T0, entries);

        Assert.Equal(T0, snapshot.Now);
        Assert.True(snapshot.TryGet("cpu.temp", out var temp));
        Assert.Equal(52, temp.Number);
        Assert.Equal([50.0, 52.0], snapshot.History("cpu.temp"));
        Assert.False(snapshot.TryGet("gpu.temp", out _));
        Assert.True(snapshot.TryGetLast("gpu.temp", out _, out var stale));
        Assert.True(stale);
        Assert.Equal(["cpu.temp", "gpu.temp"], snapshot.Keys);
    }

    [Fact]
    public void The_entries_are_copied()
    {
        var entries = new Dictionary<string, SensorEntry> { ["cpu.temp"] = new(SensorValue.Of(52), T0, null, []) };
        var snapshot = SensorSnapshot.Of(T0, entries);

        entries.Clear();

        Assert.True(snapshot.TryGet("cpu.temp", out _));
    }
}
