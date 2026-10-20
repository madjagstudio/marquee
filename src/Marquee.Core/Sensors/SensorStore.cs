namespace Marquee.Core.Sensors;

/// Latest value per key. Providers write from their own threads; the render loop takes one snapshot per frame.
/// Numeric values also keep a fixed-length history, oldest first, for graph widgets. A string write clears that history.
public sealed class SensorStore(Func<DateTimeOffset>? now = null, int historyLength = SensorStore.DefaultHistoryLength)
{
    /// Enough for the longest graph a theme may draw (GraphParser.MaxPoints), one sample per write.
    public const int DefaultHistoryLength = 600;

    readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    readonly object _gate = new();
    readonly Dictionary<string, SensorEntry> _entries = new(StringComparer.Ordinal);

    /// Stores a number. With a ttl, the value counts as stale once that much time has passed without a new write.
    /// NaN and infinities (a provider reporting a sensor it couldn't read) are ignored: the previous value stays until
    /// its ttl, then goes stale.
    public void Set(string key, double value, TimeSpan? ttl = null)
    {
        if (double.IsFinite(value))
            Write(key, SensorValue.Of(value), ttl);
    }

    public void Set(string key, string value, TimeSpan? ttl = null) => Write(key, SensorValue.Of(value), ttl);

    public SensorSnapshot Snapshot()
    {
        lock (_gate)
            return new SensorSnapshot(_now(), new Dictionary<string, SensorEntry>(_entries, StringComparer.Ordinal));
    }

    /// Removes the keys starting with prefix whose ttl ran out at least staleFor ago, and returns them. Entries are
    /// otherwise kept forever, which is fine for a provider's fixed set of keys but not for keys that scripts make up:
    /// this lets the custom-values server forget the ones nobody updates any more. Keys without a ttl are never removed.
    public IReadOnlyList<string> RemoveStale(string prefix, TimeSpan staleFor)
    {
        // Expired at or before this; subtracting from now can't overflow the way adding to a far-off expiry could.
        var cutoff = _now() - staleFor;
        lock (_gate)
        {
            var stale = _entries
                .Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal) && e.Value.ExpiresAt <= cutoff)
                .Select(e => e.Key)
                .ToList();
            foreach (var key in stale) _entries.Remove(key);
            return stale;
        }
    }

    /// The keys starting with prefix, stale or not, as a set of their own.
    public IReadOnlySet<string> Keys(string prefix)
    {
        lock (_gate)
            return _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
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
