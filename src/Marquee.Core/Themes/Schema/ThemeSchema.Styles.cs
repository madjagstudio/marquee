namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    /// Defaults here are the renderer's fallbacks: the parser leaves a key a style doesn't set unset, so a group's
    /// style, the widget's and its rules can layer.
    static SchemaBlock StyleBlock() => new("style", "style", "Styles",
        "How a widget looks. A style is named in `styles:`, or written in place as a widget's or rule's `style:`. A group "
        + "passes its text keys (font, size, weight, color, align, letter_spacing, line_height, wrap) down to its children; the others stay with its box.",
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
                "The text's color, and the filled part of a bar, gauge or graph line when `fill` or `stroke` doesn't set one. A "
                + "dial's needle and ticks, and a donut's slices, use it when they set no color of their own.")
            {
                Default = "#FFFFFF",
            },
            new("fill", SchemaKind.Paint,
                "The background of a text, value, image or group box, and the inside of a shape. On a bar or gauge, the filled "
                + "part or the lit segments, and on a donut, each slice (the style's color when left out); on a graph, the area "
                + "under the line or the columns (an area or columns graph uses the color without it)."),
            new("track", SchemaKind.Paint,
                "The empty part of a bar or gauge, or its unlit segments, the background behind a graph, and the ring behind a "
                + "donut's slices."),
            new("stroke", SchemaKind.Block, "An outline round a box, a bar, a shape or a donut's slice (a line shape is the stroke "
                + "alone), a graph's line, and the ends of a gauge's arc, an arc shape or a line (its cap).")
            {
                Of = "style.stroke",
            },
            new("shadow", SchemaKind.Block, "A shadow under text, a shape, a gauge's needle or a donut's slices.") { Of = "style.shadow" },
            new("glow", SchemaKind.Block, "A soft glow round text, a shape or a donut's slices.") { Of = "style.glow" },
            new("pulse", SchemaKind.Block, "Makes the opacity or the glow beat steadily.") { Of = "style.pulse" },
            new("opacity", SchemaKind.Number, "How solid the whole widget is: 0 hides it, 1 is fully solid.")
            {
                Default = Number(StyleDefaults.Opacity), Min = 0, Max = 1, Unit = SchemaUnit.Fraction,
            },
            new("radius", SchemaKind.Number,
                "Rounds the corners of a box, a bar, the segments of a bar or gauge, a rect shape, a graph's background and what "
                + "a group or image clips to.")
            {
                Default = Number(StyleDefaults.Radius), Min = 0, Unit = SchemaUnit.Px,
            },
            new("padding", SchemaKind.Number, "Space between a text or value widget's box and its text.")
            {
                Default = Number(StyleDefaults.Padding), Min = 0, Unit = SchemaUnit.Px,
            },
            new("align", SchemaKind.Enum,
                "Where each line of text sits across its box. The lines together are always centred from top to bottom.")
            {
                Default = "left", Values = ["left", "center", "right"],
            },
            new("blend", SchemaKind.Enum,
                "How the whole widget mixes with what's under it, as a background layer's blend does. A group's children "
                + "blend with it, as one. Inside a group with an opacity below 1 or a blend of its own, a widget's blend "
                + "mixes with the group's layer, not with what's behind the group.")
            {
                Default = "normal", Values = BlendModes,
            },
            new("letter_spacing", SchemaKind.Number, "Space added between the characters of text; below 0 draws them closer.")
            {
                Default = Number(StyleDefaults.LetterSpacing), Min = -20, Max = 100, Unit = SchemaUnit.Px,
            },
            new("line_height", SchemaKind.Number, "How far apart the lines of text are, as a multiple of the text's size.")
            {
                Default = Number(StyleDefaults.LineHeight), Min = 0.5, Max = 4, Unit = SchemaUnit.Multiplier,
            },
            new("wrap", SchemaKind.Boolean,
                "Breaks text too wide for its box onto more lines, at spaces. Without it, such text draws on one line past its "
                + "box. A line break starts a new line either way.")
            {
                Default = "false",
            },
        ]);
}
