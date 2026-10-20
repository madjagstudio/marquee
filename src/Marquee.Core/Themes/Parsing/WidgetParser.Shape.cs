using SkiaSharp;

namespace Marquee.Core.Themes.Parsing;

/// The shape widget: `shape: rect | ellipse | line | arc`, and an arc's own keys.
static partial class WidgetParser
{
    /// The keys only an arc takes. A property rather than a field, so TypeKeys can use it whichever order the partial
    /// files' static fields are initialized in.
    internal static string[] ArcKeys => ["start", "sweep", "thickness"];

    static readonly (string Name, ShapeKind Value)[] Shapes =
        [("rect", ShapeKind.Rect), ("ellipse", ShapeKind.Ellipse), ("line", ShapeKind.Line), ("arc", ShapeKind.Arc)];

    static ShapeWidget Shape(Fields f)
    {
        var kind = Yaml.Choice(f.Required("shape"), "shape", Shapes);
        if (kind != ShapeKind.Arc)
        {
            foreach (var key in ArcKeys)
                if (f.Optional(key) is { } node)
                    throw Yaml.Error(node, $"{key} only applies to an arc, and this shape is {Article(kind)}");
            return new ShapeWidget(kind);
        }

        // As a gauge's arc takes them, with the same defaults, but always at least 1 px thick: it has no needle.
        var (start, sweep, thickness) = Arc(f, minThickness: 1);
        return new ShapeWidget(kind, start, sweep, thickness);
    }

    /// A shape's size, which Parse reads before the shape's other keys: a line's box may be 0 wide or high, though not
    /// both, and every other shape's is at least 1 each way, as other widgets' are.
    static SKPoint ShapeSize(Fields f)
    {
        var node = f.Required("size");
        if (Yaml.Choice(f.Required("shape"), "shape", Shapes) != ShapeKind.Line)
            return Yaml.Pair(node, "size", min: 1);
        var size = Yaml.Pair(node, "size", min: 0);
        return size is { X: 0, Y: 0 } ? throw Yaml.Error(node, "A line needs a length: its size can't be 0 both ways") : size;
    }

    /// An arc's `start`, `sweep` and `thickness`, with the ranges and defaults (Arcs) that a gauge's arc and an arc shape
    /// share. Only how thin the band may be differs.
    static (float Start, float Sweep, float Thickness) Arc(Fields f, float minThickness)
    {
        var start = f.Optional("start") is { } a ? Yaml.Float(a, "start", -Arcs.MaxAngle, Arcs.MaxAngle) : Arcs.DefaultStart;
        var sweep = Arcs.DefaultSweep;
        if (f.Optional("sweep") is { } s)
        {
            sweep = Yaml.Float(s, "sweep", -Arcs.MaxAngle, Arcs.MaxAngle);
            if (sweep == 0) throw Yaml.Error(s, "sweep must not be 0");
        }

        var thickness = f.Optional("thickness") is { } t ? Yaml.Float(t, "thickness", minThickness) : Arcs.DefaultThickness;
        return (start, sweep, thickness);
    }

    static string Article(ShapeKind kind) => kind == ShapeKind.Ellipse ? "an ellipse" : $"a {kind.ToString().ToLowerInvariant()}";

    /// Only the stroke draws a line, so one that nothing strokes would never show. A `when` rule's stroke counts: such a
    /// line shows only while the rule holds. A box that nothing fills or strokes is allowed, as it is for other widgets.
    static void CheckStroked(ShapeWidget shape, Fields f)
    {
        static bool Stroked(StyleSpec style) => style.Stroke?.Paint is not null;
        if (shape.Shape == ShapeKind.Line && !Stroked(shape.Style) && !shape.When.Any(rule => Stroked(rule.Style)))
            throw Yaml.Error(f.Node, "A line is drawn by its stroke alone, but nothing gives it a stroke color: set one in its "
                + "style, such as stroke: { width: 2, color: \"#FFFFFF\" }");
    }
}
