namespace Marquee.Host.SensorFeed;

/// One key's last value, a number or a text, and whether it has gone stale (its ttl ran out without a new write).
public sealed record FeedValue(double? Number, string? Text, bool Stale);

/// Every key in Marquee's sensor store with its last value, custom values pushed with `marquee push` included, and the
/// store's clock when it was read. Truncated says keys were left out to keep the message under its cap
/// (SensorFeedFormat).
public sealed record SensorFeedValues(int Version, DateTimeOffset Now, IReadOnlyDictionary<string, FeedValue> Values,
    bool Truncated = false);

/// The graph history of every numeric key, oldest point first, up to the store's history length. Truncated says the
/// oldest points were left out to keep the message under its cap (SensorFeedFormat).
public sealed record SensorFeedHistory(int Version, DateTimeOffset Now, IReadOnlyDictionary<string, IReadOnlyList<double>> History,
    bool Truncated = false);
