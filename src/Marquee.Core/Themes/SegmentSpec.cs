namespace Marquee.Core.Themes;

/// A bar or gauge cut into Count equal segments, like a VU meter, with Gap between each pair: px along a bar, degrees
/// around a gauge. A full ring also has a gap at the join, so its first and last segments don't touch.
public sealed record SegmentSpec(int Count, float Gap)
{
    public const int MinCount = 2;

    /// The format's limit, so a meter stays something a renderer draws block by block in a frame.
    public const int MaxCount = 120;

    public const float DefaultBarGap = 4;
    public const float DefaultGaugeGap = 3;

    /// The shortest a segment may be, so no gap leaves it too small to see: 1 px along a bar, 1° around a gauge.
    public const float MinLength = 1;
}
