using Marquee.Core.Sensors;
using Marquee.Editor.Model.Values;
using Marquee.Host.SensorFeed;

namespace Marquee.Editor.Model.Tests.TestSupport;

/// A tray feed that answers what the test sets, and counts the reads.
sealed class FakeFeed : ITrayFeed
{
    static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    int _valueReads;
    int _historyReads;

    public SensorFeedStatus Status { get; set; } = SensorFeedStatus.NotRunning;
    public Dictionary<string, FeedValue> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, IReadOnlyList<double>> History { get; } = new(StringComparer.Ordinal);

    public int ValueReads => Volatile.Read(ref _valueReads);
    public int HistoryReads => Volatile.Read(ref _historyReads);

    public Task<SensorFeedResult<SensorFeedValues>> ReadValuesAsync(TimeSpan timeout, CancellationToken cancel = default)
    {
        Interlocked.Increment(ref _valueReads);
        var message = new SensorFeedValues(1, T0, new Dictionary<string, FeedValue>(Values, StringComparer.Ordinal));
        return Task.FromResult(new SensorFeedResult<SensorFeedValues>(Status, Status == SensorFeedStatus.Ok ? message : null));
    }

    public Task<SensorFeedResult<SensorFeedHistory>> ReadHistoryAsync(TimeSpan timeout, CancellationToken cancel = default)
    {
        Interlocked.Increment(ref _historyReads);
        var message = new SensorFeedHistory(1, T0, new Dictionary<string, IReadOnlyList<double>>(History, StringComparer.Ordinal));
        return Task.FromResult(new SensorFeedResult<SensorFeedHistory>(Status, Status == SensorFeedStatus.Ok ? message : null));
    }

    public void Set(string key, double number, bool stale = false) => Values[key] = new FeedValue(number, null, stale);

    public void Set(string key, string text) => Values[key] = new FeedValue(null, text, false);
}

/// Local readings from a store the test writes, with no sensors behind it.
sealed class FakeLocal : ILocalReadings
{
    int _starts;
    int _stops;

    public SensorStore Store { get; } = new(() => new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    public bool IsRunning { get; private set; }
    public int Starts => Volatile.Read(ref _starts);
    public int Stops => Volatile.Read(ref _stops);
    public bool Disposed { get; private set; }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        Interlocked.Increment(ref _starts);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        Interlocked.Increment(ref _stops);
    }

    public SensorSnapshot Snapshot() => Store.Snapshot();

    public void Dispose()
    {
        Stop();
        Disposed = true;
    }
}
