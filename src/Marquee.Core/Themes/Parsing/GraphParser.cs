using Marquee.Core.Expressions;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// A graph widget's own keys: its series (one `source`, or a `series:` list), how it draws them, and its gridlines.
static class GraphParser
{
    // The keys a graph and its nested blocks accept. ThemeSchema describes each, and its drift test reads these lists.
    internal static readonly string[] Keys = ["min", "max", "points", "kind", "grid", "series"];
    internal static readonly string[] GridKeys = ["lines", "color", "width"];
    internal static readonly string[] SeriesKeys = ["source", "style"];

    /// Most samples a graph spans. SensorStore keeps at least this much history for every key.
    public const int MaxPoints = 600;

    public const int DefaultPoints = 60;

    /// The fewest and most series one graph draws.
    public const int MinSeries = 1;
    public const int MaxSeries = 8;

    public const int MaxGridLines = 20;
    public const int DefaultGridLines = 4;

    /// White at 15%: visible on a dark track without competing with the data.
    public static readonly SKColor DefaultGridColor = new(0xFF, 0xFF, 0xFF, 0x26);

    public const float DefaultGridWidth = 1;

    public static GraphWidget Parse(Fields f, StyleTable styles, ParseErrors errors)
    {
        IReadOnlyList<GraphSeries> series = f.OneOf("source", "series") == "source"
            ? [new GraphSeries(Key(f.Required("source")), StyleSpec.Empty)]
            : ListParser.Entries(f.Required("series"), "series", MinSeries, MaxSeries, errors, entry => Series(entry, styles));
        var min = f.Optional("min") is { } a ? Yaml.Number(a, "min") : (double?)null;
        var max = f.Optional("max") is { } b ? Yaml.Number(b, "max") : (double?)null;
        if (min is { } lo && max is { } hi && hi <= lo)
            throw Yaml.Error(f.Optional("max")!, "graph widget max must be greater than min");
        var points = f.Optional("points") is { } p ? Yaml.Integer(p, "points", 2, MaxPoints) : DefaultPoints;
        var kind = f.Optional("kind") is { } k
            ? Yaml.Choice(k, "kind", ("line", GraphKind.Line), ("area", GraphKind.Area), ("columns", GraphKind.Columns))
            : GraphKind.Line;
        var grid = f.Optional("grid") is { } g ? Grid(g) : null;
        return new GraphWidget(series, min, max, points) { Kind = kind, Grid = grid };
    }

    /// Each series keeps its own history, which the sensor store holds per key, so a source must be one key. One that
    /// doesn't parse at all is a mistake with its line, as any widget's source is.
    static string Key(YamlNode node) =>
        WidgetParser.Source(node) is KeyExpr k
            ? k.Key
            : throw Yaml.Error(node, "A graph's source must be a single sensor key, such as gpu.load");

    static GraphSeries Series(YamlNode node, StyleTable styles)
    {
        var f = Yaml.Map(node, "graph series", SeriesKeys);
        var style = f.Optional("style") is { } s ? styles.Reference(s, "style") : StyleSpec.Empty;
        return new GraphSeries(Key(f.Required("source")), style);
    }

    static GraphGrid Grid(YamlNode node)
    {
        var f = Yaml.Map(node, "grid", GridKeys);
        return new GraphGrid(
            f.Optional("lines") is { } lines ? Yaml.Integer(lines, "grid.lines", 1, MaxGridLines) : DefaultGridLines,
            f.Optional("color") is { } color ? PaintParser.Color(color, "grid.color") : DefaultGridColor,
            f.Optional("width") is { } width ? Yaml.Float(width, "grid.width", 0) : DefaultGridWidth);
    }
}
