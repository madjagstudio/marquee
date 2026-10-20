using Marquee.Core.Expressions;

namespace Marquee.Core.Themes;

public enum DonutLabels { Outside, Inside, None }

/// A pie or donut that splits a whole between its slices, each by its share of the live slices' total. Angles are
/// degrees clockwise from 3 o'clock, as a gauge's are, so the default start of -90 is 12 o'clock. Hole is a fraction of
/// the radius (0 is a pie) and Gap the degrees between neighbouring slices. Format writes a slice's share, in percent,
/// after its label; null leaves the share out.
public sealed record DonutWidget(float Hole, float StartAngle, float Gap, DonutLabels Labels, string? Format,
    IReadOnlyList<DonutSlice> Slices) : WidgetSpec
{
    public const float DefaultHole = 0.6f;
    public const float DefaultStart = -90;
    public const float DefaultGap = 2;
}

/// One slice: its value, the text its label shows, and its own fill and stroke, layered over the widget's style.
public sealed record DonutSlice(Expression Source, string? Label, StyleSpec Style);
