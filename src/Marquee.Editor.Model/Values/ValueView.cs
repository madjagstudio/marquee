using Marquee.Core.Sensors;
using Marquee.Host.SensorFeed;

namespace Marquee.Editor.Model.Values;

/// What the tray last sent: its values, the history kept for the theme's keys, and when it came.
sealed record Tray(IReadOnlyDictionary<string, FeedValue> Values, IReadOnlyDictionary<string, double[]> History, DateTimeOffset At);

/// Every source as it was at one instant, from which a frame's values, or the panel's lines, are made: so one frame
/// never mixes two polls. Now is the snapshot's clock: the pinned time, or the PC's.
sealed class ValueView(TestValues test, Tray? tray, SensorSnapshot local, IReadOnlyDictionary<string, TestPin> pins,
    DateTimeOffset now, bool nowPinned)
{
    public IReadOnlyList<string> Keys { get; } = test.Keys;

    public DateTimeOffset Now => now;

    /// The key's entry, null when it is missing, and where it came from.
    public (SensorEntry? Entry, ValueSourceKind Source) Resolve(string key)
    {
        if (key == TestValues.Now)
            return (Entry(SensorValue.Of(now.ToUnixTimeSeconds()), false, []), nowPinned ? ValueSourceKind.Pinned : ValueSourceKind.Local);

        var (entry, source) = Underneath(key);
        return pins.GetValueOrDefault(key) switch
        {
            { Kind: PinKind.Number } pin => (Entry(SensorValue.Of(pin.Number), false, test.HistoryOf(key, pin)), ValueSourceKind.Pinned),
            { Kind: PinKind.Stale } => (entry with { ExpiresAt = now }, ValueSourceKind.Pinned),
            { Kind: PinKind.Missing } => (null, ValueSourceKind.Pinned),
            _ => (entry, source),
        };
    }

    /// The key's value from the tray or the editor's own readings, stale or not; null when neither has it. A value that
    /// changes here is one the canvas has to redraw for.
    public (SensorValue Value, bool Stale)? Fed(string key)
    {
        if (tray is not null && tray.Values.TryGetValue(key, out var fed)) return (ValueOf(fed), fed.Stale);
        if (local.TryGetLast(key, out var value, out var stale)) return (value, stale);
        return null;
    }

    /// The key's value without its pin: the tray's, then the editor's own, then a sample.
    (SensorEntry Entry, ValueSourceKind Source) Underneath(string key)
    {
        if (tray is not null && tray.Values.TryGetValue(key, out var fed))
            return (Entry(ValueOf(fed), fed.Stale, tray.History.GetValueOrDefault(key) ?? []), ValueSourceKind.Tray);
        if (local.TryGetLast(key, out var value, out var stale))
            return (Entry(value, stale, local.History(key)), ValueSourceKind.Local);

        var sample = Samples.ValueFor(key, now);
        var points = test.GraphPoints(key);
        IReadOnlyList<double> history = points > 0 ? Samples.HistoryFor(key, now, points)
            : sample.Number is { } number ? [number] : [];
        return (Entry(sample, false, history), ValueSourceKind.Sample);
    }

    /// An entry that reads as stale at Now, or never goes stale.
    SensorEntry Entry(SensorValue value, bool stale, IReadOnlyList<double> history) =>
        new(value, now, stale ? now : null, history as double[] ?? [.. history]);

    static SensorValue ValueOf(FeedValue fed) => fed.Number is { } number ? SensorValue.Of(number) : SensorValue.Of(fed.Text ?? "");
}
