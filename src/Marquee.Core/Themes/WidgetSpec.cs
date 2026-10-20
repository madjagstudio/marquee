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

    /// Degrees clockwise about the widget's own centre. A group's children turn with it.
    public float Rotate { get; init; }

    /// Not drawn, and its assets not loaded; `marquee validate` still checks it. A hidden group hides its children.
    public bool Hidden { get; init; }

    /// For the editor, which won't move or resize a locked widget with the mouse. The renderer ignores it.
    public bool Locked { get; init; }
}

/// Static text or a template such as "{claude.sessions.busy}/{claude.sessions.total} working".
public sealed record TextWidget(Template Text) : WidgetSpec;

/// One value. Format is a .NET composite format ("{0:0.0}°C") or a helper name ("duration", "bytes").
public sealed record ValueWidget(string? Format) : WidgetSpec;

public enum BarDirection { Horizontal, Vertical }

/// Fills left → right (horizontal) or bottom → top (vertical) between Min and Max.
public sealed record BarWidget(double Min, double Max, BarDirection Direction) : WidgetSpec
{
    /// Null draws one continuous fill.
    public SegmentSpec? Segments { get; init; }
}

/// A ring or arc. Angles are degrees clockwise from 3 o'clock; the default arc opens at the bottom.
public sealed record GaugeWidget(double Min, double Max, float StartAngle, float SweepAngle, float Thickness) : WidgetSpec
{
    /// Null draws one continuous arc.
    public SegmentSpec? Segments { get; init; }
}

/// A sparkline over the last Points samples of one sensor key. Min/Max fix the vertical scale; null scales to the data.
public sealed record GraphWidget(string Key, double? Min, double? Max, int Points) : WidgetSpec;

/// Path is relative to the theme folder.
public sealed record ImageWidget(string Path, ImageFit Fit) : WidgetSpec;

/// Positions and clips its children. Text properties of its style cascade to them.
public sealed record GroupWidget(IReadOnlyList<WidgetSpec> Children) : WidgetSpec;

public enum ShapeKind { Rect, Ellipse, Line, Arc }

/// A plain shape drawn with its style's fill, stroke, shadow and glow. A line runs from the box's top-left to its
/// bottom-right corner. StartAngle, SweepAngle and Thickness are an arc's, in degrees clockwise from 3 o'clock as a
/// gauge's are; the other shapes keep the defaults and don't use them.
public sealed record ShapeWidget(ShapeKind Shape, float StartAngle = Arcs.DefaultStart, float SweepAngle = Arcs.DefaultSweep,
    float Thickness = Arcs.DefaultThickness) : WidgetSpec;

/// What a gauge's arc and an arc shape share: angles in degrees clockwise from 3 o'clock, a start and a sweep of at most
/// a whole turn either way, and the defaults that open the arc at the bottom.
public static class Arcs
{
    public const float DefaultStart = 135;
    public const float DefaultSweep = 270;
    public const float DefaultThickness = 12;
    public const float MaxAngle = 360;
}
