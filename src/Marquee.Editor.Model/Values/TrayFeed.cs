using Marquee.Host.SensorFeed;

namespace Marquee.Editor.Model.Values;

/// The tray's sensor feed as the value sources read it: SensorFeedClient's asynchronous reads, behind an interface so
/// tests can answer for the tray.
public interface ITrayFeed
{
    Task<SensorFeedResult<SensorFeedValues>> ReadValuesAsync(TimeSpan timeout, CancellationToken cancel = default);

    Task<SensorFeedResult<SensorFeedHistory>> ReadHistoryAsync(TimeSpan timeout, CancellationToken cancel = default);
}

/// The real feed: the pipes of this user's tray, or the ones `client` was made with.
public sealed class TrayFeed(SensorFeedClient client) : ITrayFeed
{
    public TrayFeed()
        : this(new SensorFeedClient())
    {
    }

    public Task<SensorFeedResult<SensorFeedValues>> ReadValuesAsync(TimeSpan timeout, CancellationToken cancel = default) =>
        client.ReadValuesAsync(timeout, cancel);

    public Task<SensorFeedResult<SensorFeedHistory>> ReadHistoryAsync(TimeSpan timeout, CancellationToken cancel = default) =>
        client.ReadHistoryAsync(timeout, cancel);
}
