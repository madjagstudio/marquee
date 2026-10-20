using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// A gauge's `needle:` and `ticks:` blocks, which make it a dial.
static class DialParser
{
    // The keys each block accepts. ThemeSchema describes each, and its drift test reads these lists.
    internal static readonly string[] NeedleKeys = ["width", "length", "color", "cap"];
    internal static readonly string[] TickKeys = ["major", "minor", "length", "width", "color", "labels"];

    /// `f` is the gauge widget's own fields.
    public static GaugeWidget Dial(GaugeWidget gauge, Fields f) => gauge with
    {
        Needle = f.Optional("needle") is { } needle ? Needle(needle) : null,
        Ticks = f.Optional("ticks") is { } ticks ? Ticks(ticks) : null,
    };

    static NeedleSpec Needle(YamlNode node)
    {
        var f = Yaml.Map(node, "needle", NeedleKeys);
        return new NeedleSpec(
            f.Optional("width") is { } w ? Yaml.Float(w, "needle.width", 1) : NeedleSpec.DefaultWidth,
            f.Optional("length") is { } l ? Yaml.Float(l, "needle.length", 0.1, 1) : NeedleSpec.DefaultLength,
            f.Optional("color") is { } c ? PaintParser.Color(c, "needle.color") : null,
            f.Optional("cap") is { } cap ? Yaml.Float(cap, "needle.cap", 0) : NeedleSpec.DefaultCap);
    }

    static TickSpec Ticks(YamlNode node)
    {
        var f = Yaml.Map(node, "ticks", TickKeys);
        return new TickSpec(
            f.Optional("major") is { } major ? Yaml.Integer(major, "ticks.major", 1, TickSpec.MaxMajor) : TickSpec.DefaultMajor,
            f.Optional("minor") is { } minor ? Yaml.Integer(minor, "ticks.minor", 0, TickSpec.MaxMinor) : TickSpec.DefaultMinor,
            f.Optional("length") is { } l ? Yaml.Float(l, "ticks.length", 1) : TickSpec.DefaultLength,
            f.Optional("width") is { } w ? Yaml.Float(w, "ticks.width", 1) : TickSpec.DefaultWidth,
            f.Optional("color") is { } c ? PaintParser.Color(c, "ticks.color") : null,
            f.Optional("labels") is { } labels ? Labels(labels) : null);
    }

    /// A value widget's kind of format, or false for none, which is also what leaving it out gives. YAML's false and
    /// "false" read the same here, and no format could be the word false: it never shows the value. A word that reads
    /// like true or false in other YAML (True, no, yes) is a mistake for false, not a format.
    static string? Labels(YamlNode node)
    {
        var format = Yaml.Text(node, "ticks.labels");
        if (format == "false") return null;
        if (format.ToLowerInvariant() is "true" or "false" or "yes" or "no")
            throw Yaml.Error(node, "ticks.labels takes a format such as \"{0:0}\", or false for no labels");
        return WidgetParser.Format(node, "ticks.labels");
    }
}
