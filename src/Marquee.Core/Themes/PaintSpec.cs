using SkiaSharp;

namespace Marquee.Core.Themes;

/// How an area is filled: one color or a gradient spread across the area being painted.
public abstract record PaintSpec;

public sealed record SolidPaint(SKColor Color) : PaintSpec;

/// Angle is in degrees clockwise from left-to-right: 0 runs left → right, 90 runs top → bottom.
public sealed record LinearGradientPaint(SKColor[] Colors, float Angle) : PaintSpec
{
    /// Each color's place along the line, 0 to 1 and rising, one per color; null spreads the colors evenly.
    public float[]? Stops { get; init; }
}

/// Runs from Center outwards, to Radius times half the area's longer side.
public sealed record RadialGradientPaint(SKColor[] Colors) : PaintSpec
{
    /// The middle of the area, as fractions of its width and height.
    public static readonly SKPoint DefaultCenter = new(0.5f, 0.5f);

    public const float DefaultRadius = 1;

    /// Enough for a glow from any centre to cover the whole area: the farthest corner is at most the diagonal away,
    /// which is at most 2√2 (a square's, from a corner) in halves of the longer side.
    public const float MaxRadius = 3;

    /// Each color's place from the centre out, 0 to 1 and rising, one per color; null spreads the colors evenly.
    public float[]? Stops { get; init; }

    /// Where the gradient starts, as fractions of the area's width and height from its top-left corner.
    public SKPoint Center { get; init; } = DefaultCenter;

    /// How far the colors reach, as a fraction of half the area's longer side.
    public float Radius { get; init; } = DefaultRadius;
}
