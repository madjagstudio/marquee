namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    // A rule's style merges these blocks key by key, so it can change a stroke's color and keep its width.

    static SchemaBlock StrokeBlock() => new("style.stroke", "stroke", "Stroke",
        "A line: the outline drawn just inside a box or a shape and round a donut's slice, a line shape itself, a graph's line, "
        + "or the ends of a gauge's arc or an arc shape.",
        [
            new("width", SchemaKind.Number, "How thick the line is; 0 draws none.")
            {
                Default = Number(StyleDefaults.StrokeWidth), Min = 0, Unit = SchemaUnit.Px,
                DefaultFor = new Dictionary<string, string> { ["graph"] = Number(StyleDefaults.GraphStrokeWidth) },
            },
            new("color", SchemaKind.Paint, "The line's color or gradient. A box has no outline without it; a graph's line uses the style's color."),
            new("cap", SchemaKind.Enum, "How the ends of a gauge's arc, a graph's line, and a line or arc shape are drawn. It "
                + "doesn't shape a segmented gauge, whose segments `radius` rounds.")
            {
                Default = "butt", Values = ["butt", "round", "square"], DefaultFor = new Dictionary<string, string> { ["graph"] = "round" },
            },
        ]);

    static SchemaBlock ShadowBlock() => new("style.shadow", "shadow", "Shadow",
        "A copy of the text, a shape, a gauge's needle or a donut's slices, drawn under it, offset and softened.",
        [
            new("color", SchemaKind.Color, "The shadow's color. There is no shadow without it."),
            new("x", SchemaKind.Number, "How far right the shadow sits; negative moves it left.")
            {
                Default = Number(StyleDefaults.ShadowX), Unit = SchemaUnit.Px,
            },
            new("y", SchemaKind.Number, "How far down the shadow sits; negative moves it up.")
            {
                Default = Number(StyleDefaults.ShadowY), Unit = SchemaUnit.Px,
            },
            new("blur", SchemaKind.Number, "How soft the shadow is; 0 is sharp.")
            {
                Default = Number(StyleDefaults.ShadowBlur), Min = 0, Unit = SchemaUnit.Px,
            },
        ]);

    static SchemaBlock GlowBlock() => new("style.glow", "glow", "Glow",
        "A blurred copy of the text, a shape or a donut's slices, drawn behind it.",
        [
            new("color", SchemaKind.Color, "The glow's color. There is no glow without it."),
            new("radius", SchemaKind.Number, "How far the glow spreads.")
            {
                Default = Number(StyleDefaults.GlowRadius), Min = 0, Unit = SchemaUnit.Px,
            },
        ]);

    static SchemaBlock PulseBlock() => new("style.pulse", "pulse", "Pulse",
        "A steady beat: the opacity or the glow eases from max down to min and back, once each period.",
        [
            new("property", SchemaKind.Enum, "What beats: the widget's opacity, or its glow's radius.")
            {
                Default = "opacity", Values = ["opacity", "glow"],
            },
            new("period", SchemaKind.Number, "How long one beat takes.")
            {
                Default = Number(StyleDefaults.PulsePeriodMs), Min = 50, Unit = SchemaUnit.Ms,
            },
            new("min", SchemaKind.Number, "The low point, as a fraction of the normal opacity or glow.")
            {
                Default = Number(StyleDefaults.PulseMin), Min = 0, Max = 1, Unit = SchemaUnit.Fraction,
            },
            new("max", SchemaKind.Number, "The high point, as a fraction of the normal opacity or glow.")
            {
                Default = Number(StyleDefaults.PulseMax), Min = 0, Max = 1, Unit = SchemaUnit.Fraction,
            },
        ]);
}
