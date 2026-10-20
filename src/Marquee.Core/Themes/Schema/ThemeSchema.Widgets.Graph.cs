using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock GraphWidget() => new("widget.graph", "graph", "Graph",
        "A chart of sensors' recent values, newest at the right: one `source`, or up to 8 in `series`, on one scale. The "
        + "stroke draws a line, `fill` the area under it or the columns, and `track` the background.",
        [
            new("source", SchemaKind.SensorKey, "The sensor whose history is drawn: one key, such as gpu.load, not an expression. "
                + "It's what the graph's `when` rules compare."),
            new("series", SchemaKind.List, "Several sensors on one chart, each with its own history and style, drawn in order so "
                + "the last is in front. A graph with series takes no `when` rules: style each series instead.")
            {
                Of = "graph.series", MinItems = GraphParser.MinSeries, MaxItems = GraphParser.MaxSeries,
            },
            new("kind", SchemaKind.Enum, "How each series is drawn: a line (with the area under it if the style sets `fill`), "
                + "an area filled under a line, or one column per value.")
            {
                Default = "line", Values = ["line", "area", "columns"],
            },
            new("min", SchemaKind.Number, "The value at the bottom. Left out, the scale fits the values shown; a columns graph's "
                + "also takes in 0, so its columns rise from 0."),
            new("max", SchemaKind.Number, "The value at the top; more than min if both are set. Left out, the scale fits the "
                + "values shown (and 0, on a columns graph)."),
            new("points", SchemaKind.Integer, "How many recent values span the width, one per sensor update.")
            {
                Default = Number(GraphParser.DefaultPoints), Min = 2, Max = GraphParser.MaxPoints,
            },
            new("grid", SchemaKind.Block, "Horizontal lines at even steps of the scale, behind the data.") { Of = "graph.grid" },
        ])
    {
        ExactlyOneOf = ["source", "series"],
    };

    static SchemaBlock GraphGridBlock() => new("graph.grid", "grid", "Graph grid",
        "Horizontal lines across a graph at even steps of its scale, over the track and under the data.",
        [
            new("lines", SchemaKind.Integer, "How many lines; they split the height into one more band than this.")
            {
                Default = Number(GraphParser.DefaultGridLines), Min = 1, Max = GraphParser.MaxGridLines,
            },
            new("color", SchemaKind.Color, "The lines' color.") { Default = "#FFFFFF26" },
            new("width", SchemaKind.Number, "How thick each line is; 0 draws none.")
            {
                Default = Number(GraphParser.DefaultGridWidth), Min = 0, Unit = SchemaUnit.Px,
            },
        ]);

    static SchemaBlock GraphSeriesBlock() => new("graph.series", "series", "Graph series",
        "One sensor on a graph with several. Its style is layered over the graph's, so the graph can set the line width "
        + "once and each series its color; box keys such as `track`, `radius` and `opacity` stay the graph's.",
        [
            new("source", SchemaKind.SensorKey, "The sensor whose history this series draws: one key, not an expression.")
            {
                Required = true,
            },
            new("style", SchemaKind.Style, "How this series looks over the graph's own style: a style's name, or one written in place."),
        ]);
}
