namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    /// A paint is a color or one of these. Gradients are laid across the area being painted: a bar's gradient spans the
    /// whole bar, so the color shown follows the value.
    static SchemaUnion GradientUnion() => new("gradient", "Gradients",
        "A paint is a color or a gradient. A gradient is a mapping with `linear:` or `radial:` and two or more colors, and "
        + "goes wherever a key takes a paint: `fill`, `track`, a stroke's `color` or a fill layer.",
        new SchemaBlock("gradient.common", "gradient", "Every gradient", "Both kinds of gradient take these.",
        [
            new("stops", SchemaKind.List,
                "Where each color sits, from 0 (the line's start, or the centre) to 1 (the line's end, or the radius): one "
                + "per color, each no lower than the one before. Two equal stops make a hard edge. Left out, the colors "
                + "spread evenly.")
            {
                ItemKind = SchemaKind.Number, MinItems = 2, Min = 0, Max = 1, Unit = SchemaUnit.Fraction,
            },
        ]),
        [
            new("gradient.linear", "linear", "Linear gradient", "Colors spread in a straight line across the area.",
            [
                new("linear", SchemaKind.List, "The colors, from the start of the line to its end.")
                {
                    Required = true, ItemKind = SchemaKind.Color, MinItems = 2,
                },
                new("angle", SchemaKind.Number, "The line's direction, clockwise: 0 runs left to right, 90 top to bottom.")
                {
                    Default = "0", Unit = SchemaUnit.Degrees,
                },
            ]),
            new("gradient.radial", "radial", "Radial gradient", "Colors spread in a circle from a centre point outwards.",
            [
                new("radial", SchemaKind.List, "The colors, from the centre out to the radius.")
                {
                    Required = true, ItemKind = SchemaKind.Color, MinItems = 2,
                },
                new("center", SchemaKind.List, "Where the circle starts, as [x, y] fractions of the area: [0, 0] is its top-left corner.")
                {
                    Default = "[0.5, 0.5]", ItemKind = SchemaKind.Number, MinItems = 2, MaxItems = 2, Min = 0, Max = 1,
                    Unit = SchemaUnit.Fraction,
                },
                new("radius", SchemaKind.Number,
                    "How far the colors reach, as a fraction of half the area's longer side. 3 covers the whole area from "
                    + "any centre, even a corner.")
                {
                    Default = "1", Min = 0, Max = RadialGradientPaint.MaxRadius, Unit = SchemaUnit.Fraction,
                },
            ]),
        ]);
}
