namespace Marquee.Host.SensorFeed;

/// What came of one read of the sensor feed. Only Ok has a message; the rest say why there is none, so the editor's
/// test values panel can say "tray not running" rather than nothing.
public enum SensorFeedStatus
{
    /// The message was read.
    Ok,

    /// No Marquee of this user serves the feed: the pipe doesn't exist.
    NotRunning,

    /// The pipe exists, but no connection came free within the timeout: Marquee is serving another client, or has
    /// taken its 5 connections this second.
    Busy,

    /// Connected, but the whole message didn't arrive within the timeout.
    TimedOut,

    /// A message came, but it was cut short, empty, over its cap, malformed, or of another version.
    BadData,

    /// The pipe isn't one this user's Marquee made, so nothing is read from it: another user's, a Low-integrity
    /// process's (UserPipe.IsTrusted), one that shuts this user out, or this process has no Windows user to check it
    /// against.
    NotTrusted,
}

/// The message, when Status is Ok; otherwise null.
public sealed record SensorFeedResult<T>(SensorFeedStatus Status, T? Message) where T : class;
