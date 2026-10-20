namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    // A gauge's `needle:` and `ticks:`, which make it a dial. The dial's radius is half the largest square that fits the
    // gauge's box, the arc's outer edge.

    static SchemaBlock NeedleBlock() => new("gauge.needle", "needle", "Gauge needle",
        "A needle from the dial's centre to the value, drawn over the arc and the ticks. It eases with the gauge's "
        + "`smooth`, takes the style's shadow, and isn't drawn while the value is missing or stale.",
        [
            new("width", SchemaKind.Number, "How wide the needle is at the centre; it narrows to a point.")
            {
                Default = Number(NeedleSpec.DefaultWidth), Min = 1, Unit = SchemaUnit.Px,
            },
            new("length", SchemaKind.Number, "How far the needle reaches, as a fraction of the dial's radius.")
            {
                Default = Number(NeedleSpec.DefaultLength), Min = 0.1, Max = 1, Unit = SchemaUnit.Fraction,
            },
            new("color", SchemaKind.Color, "The needle's and its hub's color. Left out, it is the style's color."),
            new("cap", SchemaKind.Number, "The radius of a hub circle over the needle's centre; 0 draws none.")
            {
                Default = Number(NeedleSpec.DefaultCap), Min = 0, Unit = SchemaUnit.Px,
            },
        ]);

    static SchemaBlock TicksBlock() => new("gauge.ticks", "ticks", "Gauge ticks",
        "Tick marks round the inside of the arc, from min to max, with the major ticks' values inside them if `labels` "
        + "is set. Labels use the style's font, size, weight, color and letter spacing; a label that would overlap the one "
        + "before it is left out.",
        [
            new("major", SchemaKind.Integer, "How many divisions the scale has: 10 puts a major tick at every tenth of the "
                + "range, 11 ticks in all, or 10 on a full circle.")
            {
                Default = Number(TickSpec.DefaultMajor), Min = 1, Max = TickSpec.MaxMajor,
            },
            new("minor", SchemaKind.Integer, "How many minor ticks, half as long, go between each pair of major ticks.")
            {
                Default = Number(TickSpec.DefaultMinor), Min = 0, Max = TickSpec.MaxMinor,
            },
            new("length", SchemaKind.Number, "How long the major ticks are, inwards from the arc.")
            {
                Default = Number(TickSpec.DefaultLength), Min = 1, Unit = SchemaUnit.Px,
            },
            new("width", SchemaKind.Number, "How thick every tick is.")
            {
                Default = Number(TickSpec.DefaultWidth), Min = 1, Unit = SchemaUnit.Px,
            },
            new("color", SchemaKind.Color, "The ticks' color. Left out, it is the style's color."),
            new("labels", SchemaKind.Format, "How to write each major tick's value, as a value widget's `format` does, "
                + "such as \"{0:0}\". Left out, or false, the ticks have no labels.")
            {
                TakesFalse = true,
            },
        ]);
}
