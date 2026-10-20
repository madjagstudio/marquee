using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock DonutWidget() => new("widget.donut", "donut", "Donut",
        "A pie or donut that splits a whole between its slices, in the largest square that fits its box. Each slice's "
        + "angle is its share of the live slices' total, so a stale slice takes no room and shows only as a notch: its gap, "
        + "at least 2° wide. Angles are clockwise from 3 o'clock, as a gauge's are. `track` paints the whole ring behind "
        + "the slices; `fill`, `stroke`, `glow` and `shadow` style every slice, under its own style; and the text style "
        + "sets the labels.",
        [
            new("hole", SchemaKind.Number, "The hole in the middle, as a fraction of the radius; 0 is a pie.")
            {
                Default = Number(Themes.DonutWidget.DefaultHole), Min = 0, Max = DonutParser.MaxHole, Unit = SchemaUnit.Fraction,
            },
            new("start", SchemaKind.Number, "Where the first slice starts: -90 is 12 o'clock, 0 is 3 o'clock.")
            {
                Default = Number(Themes.DonutWidget.DefaultStart), Min = -Arcs.MaxAngle, Max = Arcs.MaxAngle, Unit = SchemaUnit.Degrees,
            },
            new("gap", SchemaKind.Number, "The space between neighbouring slices. All the gaps together take at most half the circle.")
            {
                Default = Number(Themes.DonutWidget.DefaultGap), Min = 0, Max = DonutParser.MaxGap, Unit = SchemaUnit.Degrees,
            },
            new("labels", SchemaKind.Enum,
                "Where each slice's label goes: outside the ring, inside it on its middle, or nowhere. A label is left "
                + "out when its slice is too thin to hold it, when it would overlap the label before it, or, inside, when it "
                + "would run past the ring. Outside labels stay inside the box across, so leave room beside the ring for them.")
            {
                Default = "none", Values = DonutParser.LabelModes.Select(m => m.Name).ToList(),
            },
            new("format", SchemaKind.Format,
                "How to write each slice's share of the whole, in percent, after its label, such as \"{0:0}%\". Left out, "
                + "labels show only the slice's label."),
            new("slices", SchemaKind.List, "The parts of the whole, drawn clockwise from start.")
            {
                Required = true, Of = "donut.slice", MinItems = DonutParser.MinSlices, MaxItems = DonutParser.MaxSlices,
            },
        ]);

    static SchemaBlock DonutSliceBlock() => new("donut.slice", "slices", "Donut slices",
        "One part of a donut's whole. Its style goes over the donut's, and the donut's `when` rules go over every slice.",
        [
            new("source", SchemaKind.Expression,
                "The slice's value: a sensor key or an expression. A value of 0 or less takes no room.")
            {
                Required = true,
            },
            new("label", SchemaKind.String, "The text the slice's label shows."),
            new("style", SchemaKind.Style,
                "The slice's `fill` (default: the style's color), `stroke`, `glow` and `shadow`: a style's name, or a style written "
                + "in place."),
        ]);
}
