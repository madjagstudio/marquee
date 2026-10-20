using System.Globalization;

namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    /// A float default as layout.yaml writes it: 0.4f is "0.4", not the double nearest it.
    static string Number(float value) => value.ToString(CultureInfo.InvariantCulture);

    /// What PaintParser.TryParseColor accepts, as a regular expression (ThemeSchemaDriftTests checks the two agree).
    public const string ColorPattern = "^(#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})|transparent)$";

    /// A kind's name in a few plain words, for tables and tooltips.
    public static string KindName(SchemaKind kind) => kind switch
    {
        SchemaKind.Number => "number",
        SchemaKind.Integer => "whole number",
        SchemaKind.Boolean => "true or false",
        SchemaKind.String => "text or number",
        SchemaKind.Enum => "choice",
        SchemaKind.Color => "color",
        SchemaKind.Paint => "paint",
        SchemaKind.FontFamily => "font",
        SchemaKind.FontWeight => "font weight",
        SchemaKind.Expression => "expression",
        SchemaKind.SensorKey => "sensor key",
        SchemaKind.Template => "text template",
        SchemaKind.Format => "format",
        SchemaKind.ThemeFile => "file",
        SchemaKind.Point => "point",
        SchemaKind.Size => "size",
        SchemaKind.StyleName => "style name",
        SchemaKind.Style => "style",
        SchemaKind.Block => "block",
        SchemaKind.List => "list",
        SchemaKind.Map => "named entries",
        SchemaKind.Uniforms => "uniforms",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// How a value of the kind is written in layout.yaml.
    public static string KindHelp(SchemaKind kind) => kind switch
    {
        SchemaKind.Number => "A number, such as 12, -4 or 0.5.",
        SchemaKind.Integer => "A whole number, such as 30.",
        SchemaKind.Boolean => "true or false.",
        SchemaKind.String => "A word or a number, such as idle or 3.",
        SchemaKind.Enum => "One of the words listed for the key.",
        SchemaKind.Color => "A quoted hex color: \"#RGB\", \"#RRGGBB\" or \"#RRGGBBAA\", where AA is the alpha, or "
            + "\"transparent\". Quote it: an unquoted # starts a YAML comment.",
        SchemaKind.Paint => "A color, or a gradient: { linear: [\"#000000\", \"#1E2A44\"], angle: 90 } or "
            + "{ radial: [\"#1E2A44\", \"#000000\"] }.",
        SchemaKind.FontFamily => "A font installed on this PC, by name, or a .ttf, .otf or .ttc file in the theme folder. "
            + "A font that isn't there falls back to Inter, with a warning.",
        SchemaKind.FontWeight => "100 to 900, or light (300), normal (400), medium (500), semibold (600) or bold (700).",
        SchemaKind.Expression => "A sensor key such as cpu.temp, or arithmetic on keys and numbers such as "
            + "\"max(cpu.load, gpu.load)\" or \"claude.five_hour.resets_at - now\".",
        SchemaKind.SensorKey => "One sensor key, such as gpu.load.",
        SchemaKind.Template => "Text with values in braces: \"{cpu.temp:0}°C\". {key:0.0} formats a number, "
            + "{key|duration} and {key|bytes} use a helper, {now|time:HH:mm} writes Unix seconds as a local time (the "
            + "PC's short time without a pattern), and {{ and }} are braces. Letters in a time pattern are pattern letters: "
            + "quote words, as in {now|time:'Today is' dddd}, and write a letter alone as %H. The pattern runs to the "
            + "closing brace, so spaces before it are kept.",
        SchemaKind.Format => "A .NET format that includes {0}, such as \"{0:0.0}°C\", or a helper: duration, bytes, or "
            + "time with an optional date and time pattern, such as \"time:HH:mm\". Letters in the pattern are pattern "
            + "letters: quote words, as in \"time:'at' HH:mm\", and write a letter alone as \"time:%H\". Spaces in it are "
            + "kept.",
        SchemaKind.ThemeFile => "A path relative to the theme folder, such as images/logo.png. It can't leave the folder.",
        SchemaKind.Point => "[x, y] in px, from the top-left corner.",
        SchemaKind.Size => "[width, height] in px.",
        SchemaKind.StyleName => "The name of a style in styles:.",
        SchemaKind.Style => "A style's name from styles:, or a style written in place, such as { color: \"#FF5A36\" }.",
        SchemaKind.Block => "A mapping with the keys listed for it.",
        SchemaKind.List => "A list, one entry per - line or [a, b] on one line.",
        SchemaKind.Map => "A mapping from names you choose to entries.",
        SchemaKind.Uniforms => "A mapping from uniform names to a number or expression, or a list of them for a "
            + "float2, float3 or float4.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
