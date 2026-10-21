using Marquee.Core.Sensors;

namespace Marquee.Host.SensorFeed;

/// The feed's messages for one store snapshot, as JSON no larger than a cap. SensorFeedFormat says how a message over
/// its cap is cut down. Cutting down encodes a message several times, which only a store far larger than any provider
/// makes (thousands of pushed keys) ever needs.
static class SensorFeedContent
{
    static readonly IReadOnlyDictionary<string, FeedValue> NoValues = new Dictionary<string, FeedValue>();
    static readonly IReadOnlyDictionary<string, IReadOnlyList<double>> NoHistory = new Dictionary<string, IReadOnlyList<double>>();

    public static byte[] Values(SensorSnapshot snapshot, int maxBytes)
    {
        var message = ValuesOf(snapshot);
        var json = SensorFeedJson.Write(message);
        if (json.Length <= maxBytes) return json;

        // Measure each key on its own: the message is the empty one plus each key's bytes, with a comma between keys.
        var cut = message with { Values = NoValues, Truncated = true };
        var empty = SensorFeedJson.Write(cut).Length;
        var kept = new Dictionary<string, FeedValue>(StringComparer.Ordinal);
        var size = empty;
        foreach (var (key, value) in message.Values.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var one = new Dictionary<string, FeedValue>(StringComparer.Ordinal) { [key] = value };
            var bytes = SensorFeedJson.Write(cut with { Values = one }).Length - empty + (kept.Count > 0 ? 1 : 0);
            if (size + bytes > maxBytes) continue;
            kept[key] = value;
            size += bytes;
        }

        return SensorFeedJson.Write(cut with { Values = kept });
    }

    public static byte[] History(SensorSnapshot snapshot, int maxBytes)
    {
        var message = HistoryOf(snapshot);
        var json = SensorFeedJson.Write(message);
        if (json.Length <= maxBytes) return json;

        // The most newest points per key that fit; fewer points never make a larger message.
        var cut = message with { Truncated = true };
        byte[]? best = null;
        var (fewest, most) = (0, message.History.Values.Select(points => points.Count).DefaultIfEmpty(0).Max() - 1);
        while (fewest <= most)
        {
            var points = fewest + (most - fewest) / 2;
            var candidate = SensorFeedJson.Write(cut with { History = Newest(message.History, points) });
            if (candidate.Length <= maxBytes)
            {
                best = candidate;
                fewest = points + 1;
            }
            else
            {
                most = points - 1;
            }
        }

        return best ?? SensorFeedJson.Write(cut with { History = NoHistory });
    }

    /// Every key, stale ones included, as SensorSnapshot.TryGetLast has it.
    public static SensorFeedValues ValuesOf(SensorSnapshot snapshot)
    {
        var values = new Dictionary<string, FeedValue>(StringComparer.Ordinal);
        foreach (var key in snapshot.Keys)
            if (snapshot.TryGetLast(key, out var value, out var stale))
                values[key] = new FeedValue(value.Number, value.Text, stale);
        return new SensorFeedValues(SensorFeedFormat.Version, snapshot.Now, values);
    }

    /// Every key with a history; a text key has none.
    public static SensorFeedHistory HistoryOf(SensorSnapshot snapshot)
    {
        var history = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        foreach (var key in snapshot.Keys)
            if (snapshot.History(key) is { Count: > 0 } points)
                history[key] = points;
        return new SensorFeedHistory(SensorFeedFormat.Version, snapshot.Now, history);
    }

    static Dictionary<string, IReadOnlyList<double>> Newest(IReadOnlyDictionary<string, IReadOnlyList<double>> history, int points) =>
        history.ToDictionary(entry => entry.Key,
            entry => (IReadOnlyList<double>)entry.Value.Skip(Math.Max(0, entry.Value.Count - points)).ToArray(),
            StringComparer.Ordinal);
}
