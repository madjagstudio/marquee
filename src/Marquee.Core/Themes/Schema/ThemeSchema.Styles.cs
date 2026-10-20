namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    /// Defaults here are the renderer's fallbacks: the parser leaves a key a style doesn't set unset, so a group's
    /// style, the widget's and its rules can layer.
    static SchemaBlock StyleBlock() => new("style", "style", "Styles",
        "How a widget looks. A style is named in `styles:`, or written in place as a widget's or rule's `style:`. A group "
        + "passes its text keys (font, size, weight, color, align) down to its children; the others stay with its box.",
        [
            new("extends", SchemaKind.StyleName, "Another named style to start from; keys set here override it."),
            new("font", SchemaKind.FontFamily,
                "A font installed on this PC, by name, or a .ttf, .otf or .ttc file in the theme folder. Inter is built in.")
            {
                Default = StyleDefaults.Font,
            },
            new("size", SchemaKind.Number, "Text size.") { Default = Number(StyleDefaults.Size), Min = 1, Max = 1000, Unit = SchemaUnit.Px },
            new("weight", SchemaKind.FontWeight, "How heavy text is: 100 to 900, or a name (light 300 to bold 700).")
            {
                Default = "normal", Min = 100, Max = 900, Values = ["light", "normal", "medium", "semibold", "bold"],
            },
            new("color", SchemaKind.Color,
                "The text's color, and the filled part of a bar, gauge or graph line when `fill` or `stroke` doesn't set one.")
            {
                Default = "#FFFFFF",
            },
            new("fill", SchemaKind.Paint,
                "The background of a text, value, image or group box. On a bar or gauge, the filled part (the style's color "
                + "when left out); on a graph, the area under the line."),
            new("track", SchemaKind.Paint, "The empty part of a bar or gauge, and the background behind a graph."),
            new("stroke", SchemaKind.Block, "An outline round a box or bar, a graph's line, and the ends of a gauge's arc (its cap).")
            {
                Of = "style.stroke",
            },
            new("shadow", SchemaKind.Block, "A shadow under text.") { Of = "style.shadow" },
            new("glow", SchemaKind.Block, "A soft glow round text.") { Of = "style.glow" },
            new("pulse", SchemaKind.Block, "Makes the opacity or the glow beat steadily.") { Of = "style.pulse" },
            new("opacity", SchemaKind.Number, "How solid the whole widget is: 0 hides it, 1 is fully solid.")
            {
                Default = Number(StyleDefaults.Opacity), Min = 0, Max = 1, Unit = SchemaUnit.Fraction,
            },
            new("radius", SchemaKind.Number, "Rounds the corners of a box, a bar, a graph's background and what a group or image clips to.")
            {
                Default = Number(StyleDefaults.Radius), Min = 0, Unit = SchemaUnit.Px,
            },
            new("padding", SchemaKind.Number, "Space between a text or value widget's box and its text.")
            {
                Default = Number(StyleDefaults.Padding), Min = 0, Unit = SchemaUnit.Px,
            },
            new("align", SchemaKind.Enum, "Where text sits across its box. It is always centred from top to bottom.")
            {
                Default = "left", Values = ["left", "center", "right"],
            },
        ]);
}
