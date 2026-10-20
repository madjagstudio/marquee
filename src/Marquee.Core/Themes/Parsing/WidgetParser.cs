using System.Globalization;
using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

static partial class WidgetParser
{
    // The keys each widget accepts: Common, plus its type's own. ThemeSchema describes each, and its drift test reads
    // these lists.
    internal static readonly string[] Common = ["type", "at", "size", "source", "style", "when", "smooth", "rotate", "hidden", "locked"];

    internal static readonly (string Name, string Value)[] Types =
        [("text", "text"), ("value", "value"), ("bar", "bar"), ("gauge", "gauge"), ("graph", "graph"), ("image", "image"),
         ("shape", "shape"), ("group", "group")];

    internal static readonly IReadOnlyDictionary<string, string[]> TypeKeys = new Dictionary<string, string[]>
    {
        ["text"] = ["text"],
        ["value"] = ["format"],
        ["bar"] = ["min", "max", "direction", .. SegmentParser.Keys],
        ["gauge"] = ["min", "max", "start", "sweep", "thickness", .. SegmentParser.Keys, "needle", "ticks"],
        ["graph"] = ["min", "max", "points"],
        ["image"] = ["src", "fit"],
        ["shape"] = ["shape", .. ArcKeys],
        ["group"] = ["children"],
    };

    /// A `when` rule's conditions, which the ring's rules share, and the keys a widget's rule accepts.
    internal static readonly string[] Conditions = ["above", "below", "equals", "stale"];
    internal static readonly string[] RuleKeys = [.. Conditions, "style"];

    /// Most widgets a layout may have, counting every level. Themes are shared, and YAML aliases can repeat a group
    /// cheaply: a few hundred bytes of aliased groups would otherwise expand into over a million widgets.
    public const int MaxWidgets = 2000;

    /// Deepest nesting of groups.
    public const int MaxGroupDepth = 16;

    /// Most degrees a widget turns either way: a whole turn.
    public const float MaxRotate = 360;

    /// What every widget in one layout shares, including how many widgets it has so far, across all levels.
    sealed class Context(StyleTable styles, ParseErrors errors)
    {
        public StyleTable Styles { get; } = styles;
        public ParseErrors Errors { get; } = errors;
        public int Widgets;

        /// Past MaxWidgets the rest of the layout isn't checked, so an aliased one can't report millions of problems.
        public bool OverLimit => Widgets > MaxWidgets;
    }

    public static IReadOnlyList<WidgetSpec> ParseList(YamlNode node, StyleTable styles, string what, ParseErrors errors) =>
        ParseList(node, what, new Context(styles, errors), groupDepth: 0);

    /// A bad widget is reported and left out, and its siblings are still checked.
    static IReadOnlyList<WidgetSpec> ParseList(YamlNode node, string what, Context context, int groupDepth)
    {
        var widgets = new List<WidgetSpec>();
        foreach (var widgetNode in Yaml.List(node, what))
        {
            if (context.OverLimit) break;
            if (context.Errors.Recover(() => Parse(widgetNode, context, groupDepth)) is { } widget)
                widgets.Add(widget);
        }

        return widgets;
    }

    /// `groupDepth` is how many groups this widget is inside.
    static WidgetSpec Parse(YamlNode node, Context context, int groupDepth)
    {
        if (++context.Widgets > MaxWidgets)
            throw Yaml.Error(node, $"The layout has more than {MaxWidgets} widgets");

        if (node is not YamlMappingNode map)
            throw Yaml.Error(node, "Each widget must be a mapping with a type, e.g. - type: value");

        var typeNode = Find(map, "type") ?? throw Yaml.Error(node, "widget is missing 'type'");
        var type = Yaml.Choice(typeNode, "widget type", Types);
        if (type == "group" && groupDepth + 1 > MaxGroupDepth)
            throw Yaml.Error(node, $"Groups are nested more than {MaxGroupDepth} levels deep");

        // When collecting, a group's children are checked before its own keys, so a mistake in the group doesn't hide
        // theirs. Throwing keeps the original order, which decides the error `marquee run` reports.
        var children = type == "group" && context.Errors.Collects && Find(map, "children") is { } c
            ? ParseList(c, "children", context, groupDepth + 1)
            : null;

        var what = $"{type} widget";
        var f = Yaml.Map(node, what, [.. Common, .. TypeKeys[type]]);
        var at = Yaml.Pair(f.Required("at"), "at");
        var size = type == "shape" ? ShapeSize(f) : Yaml.Pair(f.Required("size"), "size", min: 1);

        var source = f.Optional("source") is { } s ? Source(s) : null;
        if (source is null && type is "value" or "bar" or "gauge" or "graph")
            throw Yaml.Error(f.Node, $"{what} is missing 'source'");

        WidgetSpec widget = type switch
        {
            "text" => new TextWidget(Template(f.Required("text"))),
            "value" => new ValueWidget(f.Optional("format") is { } format ? Format(format) : null),
            "bar" => Bar(f),
            "gauge" => DialParser.Dial(Gauge(f), f),
            "graph" => Graph(f, f.Required("source")),
            "image" => new ImageWidget(Yaml.Text(f.Required("src"), "src"),
                f.Optional("fit") is { } fit ? BackgroundParser.Fit(fit, "fit") : ImageFit.Contain),
            "shape" => Shape(f),
            _ => new GroupWidget(children ?? ParseList(f.Required("children"), "children", context, groupDepth + 1)),
        };

        if (type is "bar" or "gauge")
            widget = SegmentParser.Apply(f, widget, size.X, size.Y);

        widget = widget with
        {
            Bounds = SKRect.Create(at.X, at.Y, size.X, size.Y),
            Source = source,
            Style = f.Optional("style") is { } style ? context.Styles.Reference(style, "style") : StyleSpec.Empty,
            When = f.Optional("when") is { } when ? Rules(when, context.Styles, source is not null) : [],
            SmoothMs = f.Optional("smooth") is { } smooth ? Yaml.Float(smooth, "smooth", 0, 10_000) : 0,
            Rotate = f.Optional("rotate") is { } rotate ? Yaml.Float(rotate, "rotate", -MaxRotate, MaxRotate) : 0,
            Hidden = f.Optional("hidden") is { } hidden && Yaml.Bool(hidden, "hidden"),
            Locked = f.Optional("locked") is { } locked && Yaml.Bool(locked, "locked"),
        };

        if (widget is ShapeWidget shape) CheckStroked(shape, f);
        return widget;
    }

    static YamlNode? Find(YamlMappingNode map, string key) =>
        map.Children.FirstOrDefault(kv => kv.Key is YamlScalarNode { Value: { } k } && k == key).Value;

    public static Expression Source(YamlNode node)
    {
        try
        {
            return Expression.Parse(Yaml.Text(node, "source"));
        }
        catch (ExpressionException ex)
        {
            throw Yaml.Error(node, $"source: {ex.Message}");
        }
    }

    static Template Template(YamlNode node)
    {
        try
        {
            return Expressions.Template.Parse(Yaml.Text(node, "text"));
        }
        catch (ExpressionException ex)
        {
            throw Yaml.Error(node, $"text: {ex.Message}");
        }
    }

    /// A value widget's `format`, or another key that takes one, named `what`. FormatError's reasons start with the word
    /// format, so only another key's name goes before them.
    internal static string Format(YamlNode node, string what = "format")
    {
        var format = Yaml.Text(node, what);
        return Formatting.FormatError(format) is { } error ? throw Yaml.Error(node, what == "format" ? error : $"{what}: {error}") : format;
    }

    static (double Min, double Max) Range(Fields f, double defaultMin, double defaultMax)
    {
        var min = f.Optional("min") is { } a ? Yaml.Number(a, "min") : defaultMin;
        var max = f.Optional("max") is { } b ? Yaml.Number(b, "max") : defaultMax;
        if (max <= min)
            throw Yaml.Error(f.Optional("max") ?? f.Optional("min") ?? f.Node, $"{f.What} max must be greater than min");
        return (min, max);
    }

    static BarWidget Bar(Fields f)
    {
        var (min, max) = Range(f, 0, 100);
        var direction = f.Optional("direction") is { } d
            ? Yaml.Choice(d, "direction", ("horizontal", BarDirection.Horizontal), ("vertical", BarDirection.Vertical))
            : BarDirection.Horizontal;
        return new BarWidget(min, max, direction);
    }

    static GaugeWidget Gauge(Fields f)
    {
        var (min, max) = Range(f, 0, 100);
        // As thin as 0, which draws no arc, for a dial with only a needle.
        var (start, sweep, thickness) = Arc(f, minThickness: 0);
        return new GaugeWidget(min, max, start, sweep, thickness);
    }

    static GraphWidget Graph(Fields f, YamlNode sourceNode)
    {
        var key = Expression.Parse(Yaml.Text(sourceNode, "source")) is KeyExpr k
            ? k.Key
            : throw Yaml.Error(sourceNode, "A graph's source must be a single sensor key, such as gpu.load");
        var min = f.Optional("min") is { } a ? Yaml.Number(a, "min") : (double?)null;
        var max = f.Optional("max") is { } b ? Yaml.Number(b, "max") : (double?)null;
        if (min is { } lo && max is { } hi && hi <= lo)
            throw Yaml.Error(f.Optional("max")!, "graph widget max must be greater than min");
        var points = f.Optional("points") is { } p ? Yaml.Integer(p, "points", 2, SensorStore.DefaultHistoryLength) : 60;
        return new GraphWidget(key, min, max, points);
    }

    static IReadOnlyList<WhenRule> Rules(YamlNode node, StyleTable styles, bool hasSource) =>
        Yaml.List(node, "when").Select(rule => Rule(rule, styles, hasSource)).ToList();

    static WhenRule Rule(YamlNode node, StyleTable styles, bool hasSource)
    {
        var f = Yaml.Map(node, "when rule", RuleKeys);
        var kind = f.OneOf(Conditions);
        var value = f.Required(kind);
        var style = styles.Reference(f.Required("style"), "when style");

        var (condition, number, text) = Condition(kind, value, hasSource, "widget");
        return new WhenRule(condition, number, text, style);
    }

    /// A `when` rule's condition, shared with the ring's rules: `kind` is the key the rule uses (above, below, equals or
    /// stale) and `value` that key's value. `owner` names what the rule belongs to, for a rule with nothing to compare.
    public static (WhenCondition Condition, double Number, string? Text) Condition(string kind, YamlNode value, bool hasSource,
        string owner)
    {
        if (kind != "stale" && !hasSource)
            throw Yaml.Error(value, $"when '{kind}' compares the {owner}'s source, but this {owner} has no source");

        switch (kind)
        {
            case "above":
                return (WhenCondition.Above, Yaml.Number(value, "above"), null);
            case "below":
                return (WhenCondition.Below, Yaml.Number(value, "below"), null);
            case "equals":
                var text = Yaml.Text(value, "equals");
                var number = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
                return (WhenCondition.Equals, number, text);
            default:
                return Yaml.Bool(value, "stale")
                    ? (WhenCondition.Stale, double.NaN, null)
                    : throw Yaml.Error(value, "stale only takes true; remove the rule instead of setting it to false");
        }
    }
}
