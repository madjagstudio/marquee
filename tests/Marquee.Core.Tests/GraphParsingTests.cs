using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A graph's chart kinds, gridlines and series, and their limits.
public class GraphParsingTests
{
    const string Header = """
        canvas: { orientation: landscape }
        styles:
          cpu: { stroke: { color: "#36D1DC" } }
          gpu: { stroke: { color: "#FF5A36" } }
        widgets:

        """;

    static GraphWidget Graph(string widget) => Assert.IsType<GraphWidget>(Assert.Single(Layouts.Parse(Header + widget).Widgets));

    /// Line numbers in errors count from the top of the file; the header is 5 lines long.
    static ThemeException Error(string widgets) => Layouts.Error(Header + widgets);

    [Fact]
    public void Single_source_is_one_series_drawn_as_a_line_with_the_graphs_own_style()
    {
        var graph = Graph("- { type: graph, at: [0, 0], size: [400, 100], source: gpu.load }");

        var series = Assert.Single(graph.Series);
        Assert.Equal("gpu.load", series.Key);
        Assert.Same(StyleSpec.Empty, series.Style);
        Assert.Equal(GraphKind.Line, graph.Kind);
        Assert.Null(graph.Grid);
        Assert.NotNull(graph.Source);
    }

    [Fact]
    public void Series_each_have_a_key_and_a_named_or_inline_style()
    {
        var graph = Graph("""
            - type: graph
              at: [0, 0]
              size: [400, 100]
              kind: area
              series:
                - { source: cpu.load, style: cpu }
                - { source: gpu.load, style: { fill: "#FF5A3640" } }
                - { source: ram.used_pct }
            """);

        Assert.Equal(["cpu.load", "gpu.load", "ram.used_pct"], graph.Series.Select(s => s.Key));
        Assert.Equal(new SolidPaint(new SKColor(0x36, 0xD1, 0xDC)), graph.Series[0].Style.Stroke!.Paint);
        Assert.Equal(new SolidPaint(new SKColor(0xFF, 0x5A, 0x36, 0x40)), graph.Series[1].Style.Fill);
        Assert.Same(StyleSpec.Empty, graph.Series[2].Style);
        Assert.Equal(GraphKind.Area, graph.Kind);
        // Only `source` gives a graph a subject for its `when` rules.
        Assert.Null(graph.Source);
    }

    [Theory]
    [InlineData("line", GraphKind.Line)]
    [InlineData("area", GraphKind.Area)]
    [InlineData("columns", GraphKind.Columns)]
    public void Kind_takes_line_area_or_columns(string kind, GraphKind expected) =>
        Assert.Equal(expected, Graph($"- {{ type: graph, at: [0, 0], size: [400, 100], source: gpu.load, kind: {kind} }}").Kind);

    [Fact]
    public void Grid_has_four_faint_one_pixel_lines_by_default()
    {
        var graph = Graph("- { type: graph, at: [0, 0], size: [400, 100], source: gpu.load, grid: {} }");

        Assert.Equal(new GraphGrid(4, new SKColor(0xFF, 0xFF, 0xFF, 0x26), 1), graph.Grid);
    }

    [Fact]
    public void Grid_reads_its_lines_color_and_width()
    {
        var graph = Graph("""
            - type: graph
              at: [0, 0]
              size: [400, 100]
              source: gpu.load
              grid: { lines: 3, color: "#202020", width: 2 }
            """);

        Assert.Equal(new GraphGrid(3, new SKColor(0x20, 0x20, 0x20), 2), graph.Grid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void Grid_lines_go_from_1_to_20(int lines) =>
        Assert.Equal(lines, Graph($"- {{ type: graph, at: [0, 0], size: [400, 100], source: gpu.load, grid: {{ lines: {lines} }} }}").Grid!.Lines);

    [Fact]
    public void Eight_series_and_600_points_are_the_most()
    {
        var series = string.Join("\n", Enumerable.Range(1, 8).Select(i => $"    - {{ source: s{i} }}"));
        var graph = Graph($"""
            - type: graph
              at: [0, 0]
              size: [400, 100]
              points: 600
              series:
            {series}
            """);

        Assert.Equal(8, graph.Series.Count);
        Assert.Equal(600, graph.Points);
    }

    /// The store keeps enough history for the longest graph.
    [Fact]
    public void Sensor_history_covers_the_most_points_a_graph_takes() =>
        Assert.True(SensorStore.DefaultHistoryLength >= GraphParser.MaxPoints);

    [Theory]
    [InlineData("- { type: graph, at: [0, 0], size: [10, 10], source: x, points: 601 }", 6, "points must be between 2 and 600")]
    [InlineData("- { type: graph, at: [0, 0], size: [10, 10] }", 6, "graph widget needs one of: source, series")]
    [InlineData("- { type: graph, at: [0, 0], size: [10, 10], source: x, series: [{ source: y }] }", 6,
        "graph widget can only have one of: source, series")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  kind: bars", 10,
        "kind must be one of: line, area, columns; not 'bars'")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  grid: { lines: 0 }", 10, "grid.lines must be between 1 and 20")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  grid: { lines: 21 }", 10, "grid.lines must be between 1 and 20")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  grid: { lines: 2.5 }", 10, "grid.lines must be a whole number")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  grid: { width: -1 }", 10, "grid.width must be at least 0")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  grid: { color: red }", 10,
        "grid.color: 'red' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  grid: { step: 10 }", 10,
        "Unknown key 'step' in grid. Expected one of: lines, color, width")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  grid: 4", 10, "grid must be a mapping of keys to values")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  series: cpu.load", 9, "series must be a list")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  series: []", 9, "series must have between 1 and 8 entries, not 0")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  series:\n    - { style: cpu }", 10, "graph series is missing 'source'")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  series:\n    - { source: cpu.load * 2 }", 10,
        "A graph's source must be a single sensor key, such as gpu.load")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  series:\n    - { source: cpu.load, color: \"#FF0000\" }", 10,
        "Unknown key 'color' in graph series. Expected one of: source, style")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  series:\n    - { source: cpu.load, style: hot }", 10,
        "Unknown style 'hot'. Defined styles: cpu, gpu")]
    [InlineData("- type: graph\n  at: [0, 0]\n  size: [10, 10]\n  series:\n    - { source: cpu.load }\n  when:\n    - { above: 80, style: gpu }", 12,
        "when 'above' compares the widget's source, but this widget has no source")]
    public void Graph_errors(string widgets, int line, string reason)
    {
        var ex = Error(widgets);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Fact]
    public void Nine_series_is_an_error_at_the_list()
    {
        var series = string.Join("\n", Enumerable.Range(1, 9).Select(i => $"    - {{ source: s{i} }}"));
        var ex = Error($"""
            - type: graph
              at: [0, 0]
              size: [400, 100]
              series:
            {series}
            """);

        Assert.Equal("series must have between 1 and 8 entries, not 9", ex.Reason);
        Assert.Equal(10, ex.Line);
    }

    [Fact]
    public void A_broken_graph_is_reported_and_its_siblings_still_checked()
    {
        var (spec, errors) = Layouts.Collect(Header + """
              - type: graph
                at: [0, 0]
                size: [10, 10]
                kind: pie
                series: [{ source: cpu.load }]
              - { type: graph, at: [0, 0], size: [10, 10], source: gpu.load, grid: { lines: 99 } }
              - { type: graph, at: [0, 0], size: [10, 10], series: [{ source: gpu.load }] }
            """);

        Assert.Equal(
            ["9: kind must be one of: line, area, columns; not 'pie'", "11: grid.lines must be between 1 and 20"],
            Layouts.Lines(errors));
        Assert.Single(spec.Widgets);
    }

    /// An expression that doesn't parse, where a series needs one sensor key, is a mistake with its line for both
    /// `marquee validate` and the parser `marquee run` uses, and the theme's other mistakes are still listed.
    [Fact]
    public void A_series_source_that_does_not_parse_is_an_error_at_its_line()
    {
        const string layout = """
            canvas: { orientation: landscape }
            widgets:
              - type: graph
                at: [0, 0]
                size: [10, 10]
                series: [{ source: "cpu.load +" }]
              - { type: bar, at: [0, 0], size: [10, 10] }
            """;
        using var theme = new TempTheme(layout);

        Assert.Equal(
            ["layout.yaml:6: error: source: Expression ended unexpectedly at character 11", "layout.yaml:7: error: bar widget is missing 'source'"],
            Validations.Lines(Validations.Validate(theme.Folder)));
        var ex = Layouts.Error(layout);
        Assert.Equal((6, "source: Expression ended unexpectedly at character 11"), (ex.Line, ex.Reason));
    }
}
