namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    // What the theme editor needs about each widget type beyond the keys it takes: the style keys it draws with, which
    // fill in each style key's AppliesTo, and the widget a new one starts as. Both are kept by hand from the painters:
    // a painter that starts or stops reading a style key changes its type's entry here. ThemeSchemaDriftTests checks
    // that the table names real keys and every type, and that every style key is used; ThemeSchemaExampleTests checks
    // that each example validates and renders.

    /// The style keys every widget draws with: the opacity of the whole widget, its pulse and its blend, and `extends`,
    /// which builds the style.
    internal static string[] EveryWidgetStyles => ["extends", "opacity", "pulse", "blend"];

    /// The text keys a text or value widget draws with, which a group passes down to its children (StyleSpec.Inherited).
    static string[] TextStyles => ["font", "size", "weight", "color", "align", "letter_spacing", "line_height", "wrap"];

    /// A box's fill and outline (BoxPainter), with `radius` corners.
    static string[] BoxStyles => ["fill", "stroke.width", "stroke.color", "radius"];

    /// The style keys each widget type's painter reads besides EveryWidgetStyles, in the order of WidgetUnion's types. A
    /// key of a style's stroke, shadow, glow or pulse is written as `stroke.cap`; the block's name alone means all of it.
    internal static IReadOnlyList<(string Type, string[] Keys)> WidgetStyles =>
    [
        ("text", [.. TextStyles, .. BoxStyles, "padding", "shadow", "glow"]),
        ("value", [.. TextStyles, .. BoxStyles, "padding", "shadow", "glow"]),
        ("bar", [.. BoxStyles, "color", "track"]),
        ("gauge", ["color", "fill", "track", "stroke.cap", "radius"]),
        ("graph", ["color", "fill", "track", "stroke", "radius"]),
        ("image", [.. BoxStyles]),
        ("shape", ["fill", "stroke", "shadow", "glow", "radius"]),
        ("group", [.. TextStyles, .. BoxStyles]),
    ];

    /// The widget types that draw with a style key, written as WidgetStyles writes it (`radius`, `stroke.cap`): those
    /// that list it, a key of it (`stroke.cap` for `stroke`) or its block (`stroke` for `stroke.cap`). None when every
    /// widget does.
    static IReadOnlyList<string> TypesUsing(string key)
    {
        static bool Covers(string listed, string key) =>
            listed == key || listed.StartsWith(key + ".", StringComparison.Ordinal) || key.StartsWith(listed + ".", StringComparison.Ordinal);

        return EveryWidgetStyles.Any(listed => Covers(listed, key))
            ? []
            : WidgetStyles.Where(type => type.Keys.Any(listed => Covers(listed, key))).Select(type => type.Type).ToList();
    }

    /// A style block, or one of its stroke, shadow, glow or pulse blocks, with each key's AppliesTo filled in.
    static SchemaBlock WithUse(SchemaBlock block) => block with
    {
        Keys = block.Keys.Select(key => key with { AppliesTo = TypesUsing(block.Id == "style" ? key.Name : $"{block.Name}.{key.Name}") }).ToList(),
    };

    /// The widgets with each type's Example filled in.
    static SchemaUnion WithExamples(SchemaUnion widgets) => widgets with
    {
        Variants = widgets.Variants.Select(type => type with { Example = WidgetExample(type.Name) }).ToList(),
    };

    /// A new widget of each type, at the top-left corner until the editor moves it: a size that shows it well, the keys
    /// it needs, and a sensor every PC has.
    static string? WidgetExample(string type) => type switch
    {
        "text" => """
            type: text
            at: [0, 0]
            size: [240, 40]
            text: "CPU {cpu.load:0}%"
            """,
        "value" => """
            type: value
            at: [0, 0]
            size: [120, 40]
            source: cpu.temp
            format: "{0:0}°C"
            """,
        "bar" => """
            type: bar
            at: [0, 0]
            size: [240, 16]
            source: cpu.load
            """,
        "gauge" => """
            type: gauge
            at: [0, 0]
            size: [160, 160]
            source: cpu.load
            """,
        "graph" => """
            type: graph
            at: [0, 0]
            size: [240, 80]
            source: cpu.load
            """,
        "image" => """
            type: image
            at: [0, 0]
            size: [120, 120]
            src: images/picture.png
            """,
        "shape" => """
            type: shape
            at: [0, 0]
            size: [120, 60]
            shape: rect
            style: { fill: "#3A7BD5", radius: 8 }
            """,
        "group" => """
            type: group
            at: [0, 0]
            size: [240, 120]
            children:
              - type: text
                at: [0, 0]
                size: [240, 40]
                text: Group
            """,
        _ => null,
    };
}
