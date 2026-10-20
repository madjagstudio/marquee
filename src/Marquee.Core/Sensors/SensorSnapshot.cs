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

    /// A snapshot of entries put together elsewhere than a store: the theme editor's, which mixes pinned, fed, local and
    /// sample values. The entries are copied. An entry whose ExpiresAt is at or before `now` reads as stale.
    public static SensorSnapshot Of(DateTimeOffset now, IReadOnlyDictionary<string, SensorEntry> entries) =>
        new(now, new Dictionary<string, SensorEntry>(entries, StringComparer.Ordinal));

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

    /// The last value written to the key, expired or not, and whether it has expired. False only for unknown keys.
    /// Rendering uses TryGet; the theme editor's sensor feed sends stale values too, so the editor can show what a
    /// sensor last read.
    public bool TryGetLast(string key, out SensorValue value, out bool stale)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            value = entry.Value;
            stale = entry.ExpiresAt <= Now;
            return true;
        }

        value = default;
        stale = false;
        return false;
    }

    /// Numeric history, oldest first. Empty for unknown or string-valued keys.
    public IReadOnlyList<double> History(string key) =>
        _entries.TryGetValue(key, out var entry) ? entry.History : [];
}
