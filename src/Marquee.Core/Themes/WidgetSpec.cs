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

    /// What the editor's layers list calls the widget; null when the theme gives no name. The renderer ignores it.
    public string? Name { get; init; }
}

/// Static text or a template such as "{claude.sessions.busy}/{claude.sessions.total} working".
public sealed record TextWidget(Template Text) : WidgetSpec;

/// One value. Format is a .NET composite format ("{0:0.0}°C") or a helper name ("duration", "bytes"); null shows the
/// number plainly, or the value in Spans when it has them.
public sealed record ValueWidget(string? Format) : WidgetSpec
{
    /// Written with `spans:` instead of `format`: runs in their own styles, as a SpansWidget's, where `{0}` is the value
    /// after `smooth`. Null for a value widget without them.
    public IReadOnlyList<TextSpan>? Spans { get; init; }
}

public enum BarDirection { Horizontal, Vertical }

/// Fills left → right (horizontal) or bottom → top (vertical) between Min and Max.
public sealed record BarWidget(double Min, double Max, BarDirection Direction) : WidgetSpec
{
    /// Null draws one continuous fill.
    public SegmentSpec? Segments { get; init; }
}

/// A ring or arc. Angles are degrees clockwise from 3 o'clock; the default arc opens at the bottom. GaugeDial.cs adds
/// its needle and ticks.
public sealed partial record GaugeWidget(double Min, double Max, float StartAngle, float SweepAngle, float Thickness) : WidgetSpec
{
    /// Null draws one continuous arc.
    public SegmentSpec? Segments { get; init; }
}

/// A chart of the last Points samples of each series, drawn back to front (GraphSpec.cs). Min/Max fix the vertical
/// scale of every series; null fits it to all of their data. A graph with a single `source` has one series.
public sealed record GraphWidget(IReadOnlyList<GraphSeries> Series, double? Min, double? Max, int Points) : WidgetSpec
{
    public GraphKind Kind { get; init; }
    public GraphGrid? Grid { get; init; }
}

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
