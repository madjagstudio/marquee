using Marquee.Core.Rendering;

namespace Marquee.Host;

/// What the engine is doing, for a status line: the measured rate and costs, the rate it aims for, the frames sent
/// since it last started, and the theme and screen names (null while there are none).
public sealed record EngineStatus(RenderStats Stats, int TargetFps, long FramesSent, string? Theme, string? Device)
{
    public static EngineStatus Stopped { get; } = new(new RenderStats(0, 0, 0), 0, 0, null, null);
}
