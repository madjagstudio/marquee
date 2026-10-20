namespace Marquee.Editor.Model.Values;

/// What a pinned key reads as.
public enum PinKind
{
    /// The pin's number.
    Number,

    /// Stale: the value underneath, marked stale, as a sensor that stopped updating shows.
    Stale,

    /// Missing: no value at all, as a key nothing writes shows.
    Missing,
}

/// How a pinned number fills a graph's history.
public enum HistoryPattern
{
    /// Every point the number.
    Steady,

    /// A climb from the bottom of the key's slider range up to the number.
    Ramp,

    /// The number, with a spike toward the far end of the range every few points.
    Spikes,
}

/// A test value pinned to a key in the test values panel. Pins live in memory only, and end with the session.
public sealed record TestPin(PinKind Kind, double Number = 0, HistoryPattern Pattern = HistoryPattern.Steady)
{
    public static TestPin Stale { get; } = new(PinKind.Stale);

    public static TestPin Missing { get; } = new(PinKind.Missing);

    public static TestPin Of(double number, HistoryPattern pattern = HistoryPattern.Steady) => new(PinKind.Number, number, pattern);
}
