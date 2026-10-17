namespace Marquee.Core.Sensors;

/// Latest value per key. Providers write from their own threads; the render loop takes one snapshot per frame.
/// Numeric values also keep a fixed-length history, oldest first, for graph widgets. A string write clears that history.
public sealed class SensorStore(Func<DateTimeOffset>? now = null, int historyLength = SensorStore.DefaultHistoryLength)
{
    public const int DefaultHistoryLength = 300;

    readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    readonly object _gate = new();
    readonly Dictionary<string, SensorEntry> _entries = new(StringComparer.Ordinal);

    /// Stores a number. With a ttl, the value counts as stale once that much time has passed without a new write.
    public void Set(string key, double value, TimeSpan? ttl = null) => Write(key, SensorValue.Of(value), ttl);

    public void Set(string key, string value, TimeSpan? ttl = null) => Write(key, SensorValue.Of(value), ttl);

    public SensorSnapshot Snapshot()
    {
        lock (_gate)
            return new SensorSnapshot(_now(), new Dictionary<string, SensorEntry>(_entries, StringComparer.Ordinal));
    }

    void Write(string key, SensorValue value, TimeSpan? ttl)
    {
        var at = _now();
        lock (_gate)
        {
            double[] history = [];
            if (value.Number is { } number)
                history = Append(_entries.TryGetValue(key, out var previous) ? previous.History : [], number);
            _entries[key] = new SensorEntry(value, at, at + ttl, history);
        }
    }

    // Copy-on-write, so a snapshot can share history arrays without copying them every frame.
    double[] Append(double[] history, double value)
    {
        var keep = Math.Min(history.Length, historyLength - 1);
        var next = new double[keep + 1];
        Array.Copy(history, history.Length - keep, next, 0, keep);
        next[keep] = value;
        return next;
    }
}

/// One key's latest value, when it was written, when it goes stale (if ever), and its numeric history.
/// History arrays are never modified after they are stored.
public sealed record SensorEntry(SensorValue Value, DateTimeOffset UpdatedAt, DateTimeOffset? ExpiresAt, double[] History);
