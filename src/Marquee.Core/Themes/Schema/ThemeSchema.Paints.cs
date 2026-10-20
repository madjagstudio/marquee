namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    /// A paint is a color or one of these. Gradients are laid across the area being painted: a bar's gradient spans the
    /// whole bar, so the color shown follows the value.
    static SchemaUnion GradientUnion() => new("gradient", "Gradients",
        "A paint is a color or a gradient. A gradient is a mapping with `linear:` or `radial:` and two or more colors, and "
        + "goes wherever a key takes a paint: `fill`, `track`, a stroke's `color` or a fill layer.",
        new SchemaBlock("gradient.common", "gradient", "Gradient", "", []),
        [
            new("gradient.linear", "linear", "Linear gradient", "Colors spread evenly in a straight line across the area.",
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
            new("gradient.radial", "radial", "Radial gradient", "Colors spread evenly from the centre of the area outwards.",
            [
                new("radial", SchemaKind.List, "The colors, from the centre out to half the area's longer side.")
                {
                    Required = true, ItemKind = SchemaKind.Color, MinItems = 2,
                },
            ]),
        ]);
}
