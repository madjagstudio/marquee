using Marquee.Core.Pipes;

namespace Marquee.Host.SensorFeed;

/// The names of the sensor feed's two pipes: current values, and graph history. Tests and the editor's own tests pass
/// names of their own, so they never meet the tray's.
public sealed record SensorFeedPipes(string Values, string History)
{
    /// The pipes of the Marquee running as this user, elevated or not. One pair per Windows user, since pipe names are
    /// shared by every session on the PC.
    public static SensorFeedPipes Default { get; } = For(UserPipe.CurrentUser?.Value ?? Environment.UserName);

    static SensorFeedPipes For(string user) => new($"Marquee.Values.{user}", $"Marquee.History.{user}");
}
