namespace Marquee.Host.SensorFeed;

/// What the sensor feed server allows. Tests shorten the times; the tray and `marquee run` use Default.
sealed record SensorFeedLimits(int MaxValuesBytes, int MaxHistoryBytes, TimeSpan WriteTimeout, int ConnectionsPerSecond,
    TimeSpan RetryDelay)
{
    /// A client on the same PC reads 8 MB in milliseconds, so one still reading after 2 s is stuck, or reading on
    /// purpose slowly; either way it is dropped. A pipe that can't be made is tried again each second.
    public static SensorFeedLimits Default { get; } = new(SensorFeedFormat.MaxValuesBytes, SensorFeedFormat.MaxHistoryBytes,
        WriteTimeout: TimeSpan.FromSeconds(2), ConnectionsPerSecond: 5, RetryDelay: TimeSpan.FromSeconds(1));
}
