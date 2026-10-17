using System.Globalization;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// Colors are CSS-style hex: "#RGB", "#RRGGBB" or "#RRGGBBAA", or "transparent".
/// Paints are a color, `{ linear: [colors], angle: 90 }` or `{ radial: [colors] }`.
static class PaintParser
{
    const string ColorHint = "Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"";

    public static bool TryParseColor(string text, out SKColor color)
    {
        color = default;
        if (text == "transparent")
        {
            color = new SKColor(0, 0, 0, 0); // CSS transparent is transparent black, which matters inside gradients
            return true;
        }

        if (text.Length is not (4 or 7 or 9) || text[0] != '#') return false;
        var hex = text.Length == 4 ? string.Concat(text[1..].Select(c => $"{c}{c}")) : text[1..];
        if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return false;

        color = hex.Length == 8
            ? new SKColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : new SKColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public static SKColor Color(YamlNode node, string what)
    {
        if (node is YamlScalarNode { Value: "" })
            throw Yaml.Error(node, $"{what} is empty. Quote colors, e.g. \"#FF0000\": an unquoted # starts a YAML comment");
        var text = Yaml.Text(node, what);
        return TryParseColor(text, out var color) ? color : throw Yaml.Error(node, $"{what}: '{text}' is not a color. {ColorHint}");
    }

    public static PaintSpec Paint(YamlNode node, string what)
    {
        if (node is YamlScalarNode)
            return new SolidPaint(Color(node, what));

        var fields = Yaml.Map(node, what, "linear", "radial", "angle");
        var kind = fields.OneOf("linear", "radial");
        var colors = Yaml.List(fields.Required(kind), $"{what}.{kind}")
            .Select(c => Color(c, $"{what}.{kind}"))
            .ToArray();
        if (colors.Length < 2)
            throw Yaml.Error(fields.Required(kind), $"{what}.{kind} needs at least two colors");

        if (kind == "radial")
        {
            if (fields.Optional("angle") is { } angleNode)
                throw Yaml.Error(angleNode, $"{what}: angle only applies to linear gradients");
            return new RadialGradientPaint(colors);
        }

        var angle = fields.Optional("angle") is { } a ? Yaml.Float(a, $"{what}.angle") : 0f;
        return new LinearGradientPaint(colors, angle);
    }
}
