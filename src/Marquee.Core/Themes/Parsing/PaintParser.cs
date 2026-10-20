using System.Globalization;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// Colors are CSS-style hex: "#RGB", "#RRGGBB" or "#RRGGBBAA", or "transparent".
/// Paints are a color, `{ linear: [colors], angle: 90 }` or `{ radial: [colors] }`. Either gradient takes `stops`, and a
/// radial one `center` and `radius`.
static class PaintParser
{
    const string ColorHint = "Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"";

    /// The keys a gradient accepts. ThemeSchema describes each, and its drift test reads this list.
    internal static readonly string[] GradientKeys = ["linear", "radial", "angle", "stops", "center", "radius"];

    static readonly string[] RadialOnlyKeys = ["center", "radius"];

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

        var fields = Yaml.Map(node, what, GradientKeys);
        var kind = fields.OneOf("linear", "radial");
        var colors = Yaml.List(fields.Required(kind), $"{what}.{kind}")
            .Select(c => Color(c, $"{what}.{kind}"))
            .ToArray();
        if (colors.Length < 2)
            throw Yaml.Error(fields.Required(kind), $"{what}.{kind} needs at least two colors");
        var stops = fields.Optional("stops") is { } stopsNode ? Stops(stopsNode, $"{what}.stops", colors.Length) : null;

        if (kind == "radial")
        {
            if (fields.Optional("angle") is { } angleNode)
                throw Yaml.Error(angleNode, $"{what}: angle only applies to linear gradients");
            return new RadialGradientPaint(colors)
            {
                Stops = stops,
                Center = fields.Optional("center") is { } c ? Center(c, $"{what}.center") : RadialGradientPaint.DefaultCenter,
                Radius = fields.Optional("radius") is { } r ? Yaml.Float(r, $"{what}.radius", 0, RadialGradientPaint.MaxRadius) : RadialGradientPaint.DefaultRadius,
            };
        }

        foreach (var key in RadialOnlyKeys)
            if (fields.Optional(key) is { } radialNode)
                throw Yaml.Error(radialNode, $"{what}: {key} only applies to radial gradients");
        var angle = fields.Optional("angle") is { } a ? Yaml.Float(a, $"{what}.angle") : 0f;
        return new LinearGradientPaint(colors, angle) { Stops = stops };
    }

    /// One position per color, each 0 to 1 and none below the one before: equal neighbours make a hard edge.
    static float[] Stops(YamlNode node, string what, int colors)
    {
        var items = Yaml.List(node, what);
        if (items.Count != colors)
            throw Yaml.Error(node, $"{what} needs {colors} positions, one per color, not {items.Count}");

        var stops = new float[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            stops[i] = Yaml.Float(items[i], what, 0, 1);
            if (i > 0 && stops[i] < stops[i - 1])
                throw Yaml.Error(items[i], string.Create(CultureInfo.InvariantCulture, $"{what} must rise: {stops[i]} comes after {stops[i - 1]}"));
        }

        return stops;
    }

    /// `[x, y]` as fractions of the area's width and height. A range error names the one that is out, as both share a line.
    static SKPoint Center(YamlNode node, string what)
    {
        if (node is not YamlSequenceNode { Children.Count: 2 } pair)
            throw Yaml.Error(node, $"{what} must be a pair of fractions like [0.5, 0.5]");
        return new SKPoint(Yaml.Float(pair.Children[0], $"{what}'s x", 0, 1), Yaml.Float(pair.Children[1], $"{what}'s y", 0, 1));
    }
}
