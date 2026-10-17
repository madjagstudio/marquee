using Marquee.Core.Expressions;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// Fields every widget has. Bounds are relative to the parent: the canvas, or the enclosing group.
public abstract record WidgetSpec
{
    public SKRect Bounds { get; init; }
    public Expression? Source { get; init; }
    public StyleSpec Style { get; init; } = StyleSpec.Empty;
    public IReadOnlyList<WhenRule> When { get; init; } = [];

    /// Eases numeric changes over this many milliseconds; 0 snaps.
    public float SmoothMs { get; init; }
}

/// Static text or a template such as "{claude.sessions.busy}/{claude.sessions.total} working".
public sealed record TextWidget(Template Text) : WidgetSpec;

/// One value. Format is a .NET composite format ("{0:0.0}°C") or a helper name ("duration", "bytes").
public sealed record ValueWidget(string? Format) : WidgetSpec;

public enum BarDirection { Horizontal, Vertical }

/// Fills left → right (horizontal) or bottom → top (vertical) between Min and Max.
public sealed record BarWidget(double Min, double Max, BarDirection Direction) : WidgetSpec;

/// A ring or arc. Angles are degrees clockwise from 3 o'clock; the default arc opens at the bottom.
public sealed record GaugeWidget(double Min, double Max, float StartAngle, float SweepAngle, float Thickness) : WidgetSpec;

/// A sparkline over the last Points samples of one sensor key. Min/Max fix the vertical scale; null scales to the data.
public sealed record GraphWidget(string Key, double? Min, double? Max, int Points) : WidgetSpec;

/// Path is relative to the theme folder.
public sealed record ImageWidget(string Path, ImageFit Fit) : WidgetSpec;

/// Positions and clips its children. Text properties of its style cascade to them.
public sealed record GroupWidget(IReadOnlyList<WidgetSpec> Children) : WidgetSpec;
