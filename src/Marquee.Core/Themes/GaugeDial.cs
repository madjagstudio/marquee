using SkiaSharp;

namespace Marquee.Core.Themes;

/// What turns a gauge into a dial. Both are optional; DialPainter draws them.
public sealed partial record GaugeWidget
{
    /// Points at the value, over the arc; null draws no needle.
    public NeedleSpec? Needle { get; init; }

    /// Marks, and optionally their values, inside the arc; null draws none.
    public TickSpec? Ticks { get; init; }
}

/// Width is the needle's width at the hub, in px, and it tapers to a point; Length is a fraction of the dial's radius
/// (half the largest square that fits the gauge's box); Cap is the radius of a hub circle in px, 0 for none. A null
/// Color uses the style's color.
public sealed record NeedleSpec(float Width, float Length, SKColor? Color, float Cap)
{
    public const float DefaultWidth = 4;
    public const float DefaultLength = 0.8f;
    public const float DefaultCap = 0;
}

/// Major is how many divisions the scale has, so it has Major + 1 major ticks, or Major on a full circle, where the last
/// would land on the first. Minor ticks, half as long, split each division further. Length and Width are the major
/// ticks' in px. A null Color uses the style's color. Labels is a format for the major ticks' values, as a value widget's
/// `format`, or null for no labels.
public sealed record TickSpec(int Major, int Minor, float Length, float Width, SKColor? Color, string? Labels)
{
    public const int DefaultMajor = 10;
    public const int DefaultMinor = 0;
    public const float DefaultLength = 10;
    public const float DefaultWidth = 2;

    public const int MaxMajor = 20;
    public const int MaxMinor = 10;
}
