using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// A donut widget's own keys and its slices. WidgetParser reads the keys every widget has.
static class DonutParser
{
    // ThemeSchema describes each, and its drift test reads these lists.
    internal static readonly string[] Keys = ["hole", "start", "gap", "labels", "format", "slices"];
    internal static readonly string[] SliceKeys = ["source", "label", "style"];

    public const int MinSlices = 2;
    public const int MaxSlices = 12;

    public const double MaxHole = 0.9;

    /// Wide enough for two slices to sit as far apart as they are wide; the painter keeps the gaps to half the circle.
    public const double MaxGap = 90;

    internal static readonly (string Name, DonutLabels Value)[] LabelModes =
        [("outside", DonutLabels.Outside), ("inside", DonutLabels.Inside), ("none", DonutLabels.None)];

    public static DonutWidget Parse(Fields f, StyleTable styles, ParseErrors errors)
    {
        var hole = f.Optional("hole") is { } h ? Yaml.Float(h, "hole", 0, MaxHole) : DonutWidget.DefaultHole;
        var start = f.Optional("start") is { } a ? Yaml.Float(a, "start", -Arcs.MaxAngle, Arcs.MaxAngle) : DonutWidget.DefaultStart;
        var gap = f.Optional("gap") is { } g ? Yaml.Float(g, "gap", 0, MaxGap) : DonutWidget.DefaultGap;
        var labels = f.Optional("labels") is { } l ? Yaml.Choice(l, "labels", LabelModes) : DonutLabels.None;
        var format = f.Optional("format") is { } fmt ? WidgetParser.Format(fmt) : null;
        var slices = ListParser.Entries(f.Required("slices"), "slices", MinSlices, MaxSlices, errors, slice => Slice(slice, styles));
        return new DonutWidget(hole, start, gap, labels, format, slices);
    }

    static DonutSlice Slice(YamlNode node, StyleTable styles)
    {
        var f = Yaml.Map(node, "donut slice", SliceKeys);
        return new DonutSlice(
            WidgetParser.Source(f.Required("source")),
            f.Optional("label") is { } label ? Yaml.Text(label, "label") : null,
            f.Optional("style") is { } style ? styles.Reference(style, "style") : StyleSpec.Empty);
    }
}
