using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Tests.TestSupport;

/// The fixed time the fixtures are written around: 2026-10-17 12:00 UTC.
static class Clock
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    public const double T0Unix = 1792238400;

    /// The key's number or text, or null if it is missing or stale at the store's current time.
    public static object? Read(SensorStore store, string key) =>
        store.Snapshot().TryGet(key, out var value) ? value.Number is { } n ? n : value.Text : null;
}
