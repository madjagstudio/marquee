using System.Globalization;
using System.Text;
using Marquee.Core.Sensors;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Graphs drawn as lines, areas and columns, with gridlines and several series on one chart.
public class GraphChartTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";
    static readonly SKColor Grey = new(0x80, 0x80, 0x80);

    static SKImage Render(SensorStore store, string widgets) => Frames.Render(Landscape + "widgets:\n" + widgets, store.Snapshot());

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    /// A store with each key's samples written in order, oldest first.
    static SensorStore History(params (string Key, double[] Values)[] series)
    {
        var store = TestSensors.Store();
        foreach (var (key, values) in series)
            foreach (var value in values)
                store.Set(key, value);
        return store;
    }

    [Fact]
    public void Columns_fill_one_slot_per_sample_from_the_right_with_a_gap_between()
    {
        using var frame = Render(History(("gpu.load", [100, 50])), """
              - type: graph
                at: [0, 0]
                size: [400, 100]
                source: gpu.load
                kind: columns
                min: 0
                max: 100
                points: 4
                style: { fill: "#FF0000" }
            """);

        // Four 100 px slots; each column is the slot less a 20 px gap, split either side.
        Pixels.AssertNear(SKColors.Red, At(frame, 350, 75));
        Pixels.AssertNear(SKColors.Black, At(frame, 350, 25), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 395, 75), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 300, 75), 0);
        Pixels.AssertNear(SKColors.Red, At(frame, 250, 5));
        Pixels.AssertNear(SKColors.Black, At(frame, 150, 75), 0);
    }

    [Fact]
    public void Columns_of_several_series_stand_side_by_side_in_each_slot()
    {
        using var frame = Render(History(("cpu.load", [100]), ("gpu.load", [50])), """
              - type: graph
                at: [0, 0]
                size: [400, 100]
                kind: columns
                min: 0
                max: 100
                points: 4
                series:
                  - { source: cpu.load, style: { fill: "#FF0000" } }
                  - { source: gpu.load, style: { fill: "#0000FF" } }
            """);

        // The newest slot's 80 px of columns, 310 to 390, split in two.
        Pixels.AssertNear(SKColors.Red, At(frame, 330, 20));
        Pixels.AssertNear(SKColors.Red, At(frame, 330, 75));
        Pixels.AssertNear(SKColors.Black, At(frame, 370, 20), 0);
        Pixels.AssertNear(SKColors.Blue, At(frame, 370, 75));
        Pixels.AssertNear(SKColors.Black, At(frame, 250, 75), 0);
    }

    [Theory]
    [InlineData("area", true)]
    [InlineData("line", false)]
    public void Area_fills_under_the_line_with_the_style_color_when_no_fill_is_set(string kind, bool filled)
    {
        using var frame = Render(History(("gpu.load", [50, 50])), $$"""
              - type: graph
                at: [0, 0]
                size: [400, 100]
                source: gpu.load
                kind: {{kind}}
                min: 0
                max: 100
                points: 2
                style: { color: "#00FF00", stroke: { width: 0 } }
            """);

        Pixels.AssertNear(filled ? SKColors.Lime : SKColors.Black, At(frame, 200, 75), filled ? 8 : 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 200, 25), 0);
    }

    [Fact]
    public void Area_draws_its_line_over_the_fill()
    {
        using var frame = Render(History(("gpu.load", [50, 50])), """
              - type: graph
                at: [0, 0]
                size: [400, 100]
                source: gpu.load
                kind: area
                min: 0
                max: 100
                points: 2
                style: { fill: "#0000FF", stroke: { width: 6, color: "#FF0000" } }
            """);

        Pixels.AssertNear(SKColors.Red, At(frame, 200, 50));
        Pixels.AssertNear(SKColors.Blue, At(frame, 200, 80));
    }

    [Fact]
    public void Without_min_and_max_the_scale_fits_every_series()
    {
        using var frame = Render(History(("cpu.load", [0, 10]), ("gpu.load", [90, 100])), """
              - type: graph
                at: [0, 0]
                size: [400, 100]
                points: 2
                series:
                  - { source: cpu.load, style: { stroke: { width: 4, color: "#FF0000" } } }
                  - { source: gpu.load, style: { stroke: { width: 4, color: "#0000FF" } } }
            """);

        // On one 0 to 100 scale both lines stay near their edges; scaled alone, each would cross the middle.
        Pixels.AssertNear(SKColors.Red, At(frame, 200, 95), 40);
        Pixels.AssertNear(SKColors.Blue, At(frame, 200, 5), 40);
        Pixels.AssertNear(SKColors.Black, At(frame, 200, 50), 0);
    }

    /// A column stands on the bottom of the scale, so a fitted scale would give the smallest value no column at all.
    [Theory]
    [InlineData("50, 100", "", 75, 25)]
    [InlineData("-50, -100", "", 75, 25)]
    [InlineData("50, 100", "min: 40", 95, 80)]
    public void Without_min_and_max_a_columns_scale_takes_in_0(string values, string min, int filledBelow, int emptyAbove)
    {
        using var frame = Render(History(("gpu.load", values.Split(", ").Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray())), $$"""
              - type: graph
                at: [0, 0]
                size: [400, 100]
                source: gpu.load
                kind: columns
                points: 2
                {{min}}
                style: { fill: "#FF0000" }
            """);

        // The older value's column is in the left slot. 50 of 0 to 100, or -50 of -100 to 0, is half the
        // height; 50 of 40 to 100 a sixth.
        Pixels.AssertNear(SKColors.Red, At(frame, 100, filledBelow));
        Pixels.AssertNear(SKColors.Black, At(frame, 100, emptyAbove), 0);
    }

    [Fact]
    public void A_line_scale_still_fits_the_values()
    {
        using var frame = Render(History(("gpu.load", [50, 100])), """
              - type: graph
                at: [0, 0]
                size: [400, 100]
                source: gpu.load
                points: 2
                style: { stroke: { width: 4, color: "#FF0000" } }
            """);

        // 50 is the bottom of a 50 to 100 scale.
        Pixels.AssertNear(SKColors.Red, At(frame, 4, 97), 60);
        Pixels.AssertNear(SKColors.Black, At(frame, 4, 50), 0);
    }

    /// Columns a fraction of a pixel wide or tall, at 3 samples across 100 px and a 90 px scale, still have only solid
    /// pixels: their edges are rounded to whole pixels rather than smeared.
    [Fact]
    public void Column_edges_sit_on_whole_pixels()
    {
        using var frame = Render(History(("gpu.load", [33, 33, 33])), """
              - type: graph
                at: [0, 0]
                size: [100, 90]
                source: gpu.load
                kind: columns
                min: 0
                max: 100
                points: 3
                style: { fill: "#FF0000" }
            """);

        static bool Solid(SKColor c) => c == SKColors.Red || c == SKColors.Black;
        for (var x = 0; x < 100; x++)
            Assert.True(Solid(At(frame, x, 80)), $"({x}, 80) is {At(frame, x, 80)}");
        for (var y = 0; y < 90; y++)
            Assert.True(Solid(At(frame, 15, y)), $"(15, {y}) is {At(frame, 15, y)}");
    }

    [Fact]
    public void Later_series_draw_over_earlier_ones()
    {
        using var frame = Render(History(("cpu.load", [50, 50]), ("gpu.load", [50, 50])), """
              - type: graph
                at: [0, 0]
                size: [400, 100]
                min: 0
                max: 100
                points: 2
                series:
                  - { source: cpu.load, style: { stroke: { width: 6, color: "#FF0000" } } }
                  - { source: gpu.load, style: { stroke: { width: 6, color: "#0000FF" } } }
            """);

        Pixels.AssertNear(SKColors.Blue, At(frame, 200, 50));
    }

    [Fact]
    public void A_series_style_is_layered_over_the_graphs()
    {
        using var frame = Render(History(("gpu.load", [50, 50])), """
              - type: graph
                at: [0, 0]
                size: [400, 100]
                min: 0
                max: 100
                points: 2
                style: { stroke: { width: 10, color: "#FF0000" } }
                series:
                  - { source: gpu.load, style: { stroke: { color: "#0000FF" } } }
            """);

        // The graph's 10 px width, in the series' color.
        Pixels.AssertNear(SKColors.Blue, At(frame, 200, 46));
    }

    [Fact]
    public void Gridlines_split_the_height_into_even_bands_even_before_there_is_data()
    {
        using var frame = Render(TestSensors.Store(), """
              - type: graph
                at: [0, 0]
                size: [400, 100]
                source: gpu.load
                grid: { lines: 3, color: "#808080", width: 2 }
            """);

        foreach (var y in new[] { 25, 50, 75 })
            Pixels.AssertNear(Grey, At(frame, 100, y));
        Pixels.AssertNear(SKColors.Black, At(frame, 100, 37), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 100, 5), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 100, 95), 0);
    }

    [Fact]
    public void Gridlines_are_under_the_data()
    {
        using var frame = Render(History(("gpu.load", [50, 50])), """
              - type: graph
                at: [0, 0]
                size: [400, 100]
                source: gpu.load
                kind: area
                min: 0
                max: 100
                points: 2
                grid: { lines: 3, color: "#808080", width: 2 }
                style: { fill: "#0000FF" }
            """);

        Pixels.AssertNear(SKColors.Blue, At(frame, 100, 75));
        Pixels.AssertNear(Grey, At(frame, 100, 25));
    }

    // ---- Snapshots ----

    static readonly double[] Wave = [22, 30, 41, 38, 52, 61, 58, 70, 66, 74, 81, 77, 63, 55, 49, 57, 68, 79, 88, 84, 72, 60, 51, 45, 53, 64, 71, 69, 76, 83];
    static readonly double[] Low = [18, 21, 25, 24, 30, 34, 31, 28, 26, 33, 38, 42, 40, 35, 31, 29, 34, 39, 45, 43, 37, 32, 30, 27, 29, 35, 41, 44, 40, 36];

    /// Three charts side by side, one per kind, as a dashboard at 1920×480 would set them out. `chart` is the graph's
    /// own keys, written without indentation.
    static string Panels(string chart)
    {
        var keys = string.Join("\n", chart.ReplaceLineEndings("\n").Split('\n').Select(line => "        " + line));
        var yaml = new StringBuilder(Landscape + """
            styles:
              label: { size: 24, color: "#9AA4B8", align: center }
              panel: { fill: "#FFFFFF0A", radius: 20, stroke: { width: 2, color: "#FFFFFF30" } }
            background:
              - fill: "#0B0F1A"
            widgets:

            """);
        string[] kinds = ["line", "area", "columns"];
        for (var i = 0; i < kinds.Length; i++)
            yaml.Append($$"""
                  - type: group
                    at: [{{40 + i * 620}}, 40]
                    size: [600, 400]
                    style: panel
                    children:
                      - { type: text, at: [0, 16], size: [600, 40], text: {{kinds[i].ToUpperInvariant()}}, style: label }
                      - type: graph
                        at: [30, 70]
                        size: [540, 300]
                        kind: {{kinds[i]}}

                """).Append(keys).Append('\n');
        return yaml.ToString();
    }

    /// One series of each kind with no `fill`: a line is only its stroke, and an area or columns take the color.
    [Fact]
    public void Kinds_snapshot()
    {
        using var frame = Frames.Render(Panels("""
            source: gpu.load
            min: 0
            max: 100
            points: 30
            grid: { lines: 3 }
            style: { color: "#36D1DC70", stroke: { width: 4, color: "#36D1DC" } }
            """), History(("gpu.load", Wave)).Snapshot());

        Snapshots.Match(frame, "graph-kinds");
    }

    /// Two series of each kind on one scale fitted to both, with fainter gridlines.
    [Fact]
    public void Series_snapshot()
    {
        using var frame = Frames.Render(Panels("""
            points: 30
            grid: { lines: 4, color: "#FFFFFF20" }
            style: { stroke: { width: 3 } }
            series:
              - { source: gpu.load, style: { color: "#36D1DC70", stroke: { color: "#36D1DC" } } }
              - { source: cpu.load, style: { color: "#A770EFB0", stroke: { color: "#A770EF" } } }
            """), History(("gpu.load", Wave), ("cpu.load", Low)).Snapshot());

        Snapshots.Match(frame, "graph-series");
    }
}
