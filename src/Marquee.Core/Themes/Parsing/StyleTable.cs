using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// The `styles:` section, with `extends` chains resolved. Also resolves `style:` references,
/// which are either a style name or an inline style mapping (which may itself use `extends`).
sealed class StyleTable
{
    static readonly string[] Keys =
        ["extends", "font", "size", "weight", "color", "fill", "track", "stroke", "shadow", "glow", "pulse", "opacity", "radius", "padding", "align"];

    sealed record Raw(StyleSpec Own, YamlNode? Extends, YamlNode Definition);

    readonly Dictionary<string, Raw> _raw;
    readonly Dictionary<string, StyleSpec> _resolved = new(StringComparer.Ordinal);

    StyleTable(Dictionary<string, Raw> raw) => _raw = raw;

    public static StyleTable Parse(YamlNode? node)
    {
        var raw = new Dictionary<string, Raw>(StringComparer.Ordinal);
        if (node is not null)
        {
            if (node is not YamlMappingNode map)
                throw Yaml.Error(node, "styles must be a mapping of style names to styles");
            foreach (var (keyNode, value) in map.Children)
            {
                var name = Yaml.Text(keyNode, "style name");
                var (own, extends) = ParseStyle(value, $"style '{name}'");
                raw[name] = new Raw(own, extends, keyNode);
            }
        }

        var table = new StyleTable(raw);
        foreach (var name in raw.Keys)
            table.Named(name, raw[name].Definition, []);
        return table;
    }

    public StyleSpec Reference(YamlNode node, string what)
    {
        if (node is YamlScalarNode)
            return Named(Yaml.Text(node, what), node, []);

        var (own, extends) = ParseStyle(node, what);
        return Resolve(new Raw(own, extends, node), []);
    }

    StyleSpec Named(string name, YamlNode usedAt, IReadOnlyList<string> chain)
    {
        if (_resolved.TryGetValue(name, out var resolved)) return resolved;
        if (!_raw.TryGetValue(name, out var raw))
            throw Yaml.Error(usedAt, _raw.Count == 0
                ? $"Unknown style '{name}': no styles are defined"
                : $"Unknown style '{name}'. Defined styles: {string.Join(", ", _raw.Keys)}");
        if (chain.Contains(name))
            throw Yaml.Error(raw.Definition, $"Style '{name}' extends itself: {string.Join(" → ", chain.SkipWhile(n => n != name).Append(name))}");

        var style = Resolve(raw, [.. chain, name]);
        _resolved[name] = style;
        return style;
    }

    StyleSpec Resolve(Raw raw, IReadOnlyList<string> chain) =>
        raw.Extends is null
            ? raw.Own
            : StyleSpec.Merge(Named(Yaml.Text(raw.Extends, "extends"), raw.Extends, chain), raw.Own);

    static (StyleSpec Own, YamlNode? Extends) ParseStyle(YamlNode node, string what)
    {
        var f = Yaml.Map(node, what, Keys);
        var style = new StyleSpec
        {
            Font = f.Optional("font") is { } font ? Yaml.Text(font, "font") : null,
            Size = f.Optional("size") is { } size ? Yaml.Float(size, "size", 1, 1000) : null,
            Weight = f.Optional("weight") is { } weight ? Weight(weight) : null,
            Color = f.Optional("color") is { } color ? PaintParser.Color(color, "color") : null,
            Fill = f.Optional("fill") is { } fill ? PaintParser.Paint(fill, "fill") : null,
            Track = f.Optional("track") is { } track ? PaintParser.Paint(track, "track") : null,
            Stroke = f.Optional("stroke") is { } stroke ? Stroke(stroke) : null,
            Shadow = f.Optional("shadow") is { } shadow ? Shadow(shadow) : null,
            Glow = f.Optional("glow") is { } glow ? Glow(glow) : null,
            Pulse = f.Optional("pulse") is { } pulse ? Pulse(pulse) : null,
            Opacity = f.Optional("opacity") is { } opacity ? Yaml.Float(opacity, "opacity", 0, 1) : null,
            Radius = f.Optional("radius") is { } radius ? Yaml.Float(radius, "radius", 0) : null,
            Padding = f.Optional("padding") is { } padding ? Yaml.Float(padding, "padding", 0) : null,
            Align = f.Optional("align") is { } align
                ? Yaml.Choice(align, "align", ("left", TextAlign.Left), ("center", TextAlign.Center), ("right", TextAlign.Right))
                : null,
        };
        return (style, f.Optional("extends"));
    }

    static int Weight(YamlNode node) => Yaml.Text(node, "weight") switch
    {
        "light" => 300,
        "normal" => 400,
        "medium" => 500,
        "semibold" => 600,
        "bold" => 700,
        _ => Yaml.Integer(node, "weight", 100, 900),
    };

    static StrokeSpec Stroke(YamlNode node)
    {
        var f = Yaml.Map(node, "stroke", "width", "color", "cap");
        return new StrokeSpec(
            f.Optional("width") is { } w ? Yaml.Float(w, "stroke.width", 0) : null,
            f.Optional("color") is { } c ? PaintParser.Paint(c, "stroke.color") : null,
            f.Optional("cap") is { } cap
                ? Yaml.Choice(cap, "stroke.cap", ("butt", SKStrokeCap.Butt), ("round", SKStrokeCap.Round), ("square", SKStrokeCap.Square))
                : null);
    }

    static ShadowSpec Shadow(YamlNode node)
    {
        var f = Yaml.Map(node, "shadow", "color", "x", "y", "blur");
        return new ShadowSpec(
            f.Optional("color") is { } c ? PaintParser.Color(c, "shadow.color") : null,
            f.Optional("x") is { } x ? Yaml.Float(x, "shadow.x") : null,
            f.Optional("y") is { } y ? Yaml.Float(y, "shadow.y") : null,
            f.Optional("blur") is { } b ? Yaml.Float(b, "shadow.blur", 0) : null);
    }

    static GlowSpec Glow(YamlNode node)
    {
        var f = Yaml.Map(node, "glow", "color", "radius");
        return new GlowSpec(
            f.Optional("color") is { } c ? PaintParser.Color(c, "glow.color") : null,
            f.Optional("radius") is { } r ? Yaml.Float(r, "glow.radius", 0) : null);
    }

    static PulseSpec Pulse(YamlNode node)
    {
        var f = Yaml.Map(node, "pulse", "property", "period", "min", "max");
        return new PulseSpec(
            f.Optional("property") is { } p ? Yaml.Choice(p, "pulse.property", ("opacity", PulseTarget.Opacity), ("glow", PulseTarget.Glow)) : null,
            f.Optional("period") is { } period ? Yaml.Float(period, "pulse.period", 50) : null,
            f.Optional("min") is { } min ? Yaml.Float(min, "pulse.min", 0, 1) : null,
            f.Optional("max") is { } max ? Yaml.Float(max, "pulse.max", 0, 1) : null);
    }
}
