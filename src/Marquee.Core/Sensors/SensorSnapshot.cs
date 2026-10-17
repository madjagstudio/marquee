namespace Marquee.Core.Sensors;

/// The store as it was at one instant. A frame renders entirely from one snapshot.
public sealed class SensorSnapshot
{
    readonly IReadOnlyDictionary<string, SensorEntry> _entries;

    internal SensorSnapshot(DateTimeOffset now, IReadOnlyDictionary<string, SensorEntry> entries)
    {
        Now = now;
        _entries = entries;
    }

    public DateTimeOffset Now { get; }

    public IEnumerable<string> Keys => _entries.Keys.Order(StringComparer.Ordinal);

    /// The value, if the key exists and hasn't expired. Missing and expired keys are both "stale".
    public bool TryGet(string key, out SensorValue value)
    {
        if (_entries.TryGetValue(key, out var entry) && !(entry.ExpiresAt <= Now))
        {
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// Numeric history, oldest first. Empty for unknown or string-valued keys.
    public IReadOnlyList<double> History(string key) =>
        _entries.TryGetValue(key, out var entry) ? entry.History : [];
}
