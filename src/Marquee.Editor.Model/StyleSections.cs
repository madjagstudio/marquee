namespace Marquee.Editor.Model;

/// How the properties panel groups a widget's style keys. The catalog says
/// which keys a type draws with (AppliesTo); this table only says where each goes, in the order shown. A style key the
/// catalog gains later shows under More until it is placed here, so it is never left out of the panel.
public static class StyleSections
{
    public const string Text = "Text";
    public const string Fill = "Colour and fill";
    public const string Outline = "Outline";
    public const string Effects = "Effects";
    public const string More = "More";

    /// The sections in order, each with its keys in order. `color` is the text's color, unless the type draws no text
    /// (Of says where it goes then).
    public static IReadOnlyList<(string Title, IReadOnlyList<string> Keys)> All { get; } =
    [
        (Text, ["font", "size", "weight", "color", "text_fill", "align", "letter_spacing", "line_height", "wrap", "padding",
            "italic", "underline", "strike", "case", "text_stroke"]),
        (Fill, ["fill", "track"]),
        (Outline, ["stroke"]),
        (Effects, ["shadow", "glow", "opacity", "radius", "pulse", "blend"]),
    ];

    /// The section a style key goes in. `drawsText` is false for a type that draws no text with its style (no `font`):
    /// its `color` is then the filled part's, a bar's or a graph line's, so it goes with fill and track, first.
    public static string Of(string key, bool drawsText) =>
        key == "color" && !drawsText ? Fill : All.FirstOrDefault(section => section.Keys.Contains(key)).Title ?? More;

    /// Where a key sorts among its section's keys: `color` first in Colour and fill, else the table's order, and a key the
    /// table doesn't name after the rest.
    internal static int Order(string key, bool drawsText)
    {
        if (key == "color" && !drawsText) return -1;
        foreach (var (_, keys) in All)
            if (keys.IndexOf(key) is var at and >= 0) return at;
        return int.MaxValue;
    }

    static int IndexOf(this IReadOnlyList<string> keys, string key)
    {
        for (var i = 0; i < keys.Count; i++)
            if (keys[i] == key) return i;
        return -1;
    }
}
