using SkiaSharp;

namespace Marquee.Core.Themes;

/// How an area is filled: one color or a gradient spread across the area being painted.
public abstract record PaintSpec;

public sealed record SolidPaint(SKColor Color) : PaintSpec;

/// Angle is in degrees clockwise from left-to-right: 0 runs left → right, 90 runs top → bottom.
public sealed record LinearGradientPaint(SKColor[] Colors, float Angle) : PaintSpec;

/// Runs from the center of the area outwards.
public sealed record RadialGradientPaint(SKColor[] Colors) : PaintSpec;
