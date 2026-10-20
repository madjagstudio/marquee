using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class DonutWidgetTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";
    static readonly SKColor Track = new(0x40, 0x40, 0x40);

    static SKImage Render(string widgets, params (string, object)[] values) =>
        Frames.Render(Landscape + "widgets:\n" + widgets, TestSensors.Of(values));

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    /// A 200 px pie centred on (100, 100) with no gaps: red for `a`, blue for `b`, lime for `c`.
    static string Pie(string extra = "") => $$"""
          - type: donut
            at: [0, 0]
            size: [200, 200]
            hole: 0
            gap: 0
            {{extra}}
            slices:
              - { source: a, style: { fill: "#FF0000" } }
              - { source: b, style: { fill: "#0000FF" } }
              - { source: c, style: { fill: "#00FF00" } }
        """;

    [Fact]
    public void Slices_split_the_circle_clockwise_from_12_oclock_by_share()
    {
        using var frame = Render(Pie(), ("a", 1), ("b", 2), ("c", 1));

        // a is the first quarter (12 to 3 o'clock), b the next half (3 to 9), c the last quarter.
        Pixels.AssertNear(SKColors.Red, At(frame, 150, 40));
        Pixels.AssertNear(SKColors.Blue, At(frame, 150, 160));
        Pixels.AssertNear(SKColors.Blue, At(frame, 50, 160));
        Pixels.AssertNear(SKColors.Lime, At(frame, 50, 40));
        Pixels.AssertNear(SKColors.Black, At(frame, 205, 100), 0);
    }

    [Fact]
    public void Start_turns_the_first_slice()
    {
        using var frame = Render(Pie("start: 0"), ("a", 1), ("b", 2), ("c", 1));

        // a now runs from 3 to 6 o'clock.
        Pixels.AssertNear(SKColors.Red, At(frame, 150, 160));
        Pixels.AssertNear(SKColors.Lime, At(frame, 150, 40));
    }

    [Fact]
    public void Hole_leaves_the_middle_empty()
    {
        using var frame = Render(Pie().Replace("hole: 0", "hole: 0.5"), ("a", 1), ("b", 1), ("c", 0));

        Pixels.AssertNear(SKColors.Black, At(frame, 100, 100), 0);
        Pixels.AssertNear(SKColors.Black, At(frame, 140, 100), 0);
        Pixels.AssertNear(SKColors.Red, At(frame, 175, 100));
        Pixels.AssertNear(SKColors.Blue, At(frame, 25, 100));
    }

    /// The total counts only live slices, so the others close up and fill the circle.
    [Fact]
    public void Stale_and_non_positive_slices_take_no_share()
    {
        using var stale = Render(Pie(), ("a", 1), ("c", 1));
        using var negative = Render(Pie(), ("a", 1), ("b", -5), ("c", 1));

        foreach (var frame in new[] { stale, negative })
        {
            Pixels.AssertNear(SKColors.Red, At(frame, 150, 100));
            Pixels.AssertNear(SKColors.Lime, At(frame, 50, 100));
        }
    }

    /// A stale slice keeps its gap, so it shows as a wider notch where it would be: here at 6 o'clock, between a and c.
    /// A slice of 0 has no gap of its own.
    [Fact]
    public void Stale_slice_is_drawn_as_a_gap()
    {
        using var stale = Render(Pie().Replace("gap: 0", "gap: 20"), ("a", 1), ("c", 1));
        using var zero = Render(Pie().Replace("gap: 0", "gap: 20"), ("a", 1), ("b", 0), ("c", 1));

        // Three gaps of 20° leave 300° to share: a runs from -80° to 70°, then two gaps meet around 90°, the stale b's
        // place. With b at 0 there are two gaps: a runs to 80° and one gap is centred on 90°. 75°, radius 90:
        Pixels.AssertNear(SKColors.Black, At(stale, 123, 187), 0);
        Pixels.AssertNear(SKColors.Red, At(zero, 123, 187));
        Pixels.AssertNear(SKColors.Black, At(zero, 100, 190), 0);
    }

    /// Without gaps a stale slice still shows, as a 2° notch: here at 6 o'clock, between a and c.
    [Fact]
    public void Stale_slice_is_a_notch_even_without_gaps()
    {
        using var frame = Render(Pie(), ("a", 1), ("c", 1));

        Assert.True(Pixels.Count(frame, SKRectI.Create(95, 185, 11, 1), c => c == SKColors.Black) >= 2, "no notch at 6 o'clock");
        Pixels.AssertNear(SKColors.Red, At(frame, 150, 100));
        Pixels.AssertNear(SKColors.Lime, At(frame, 50, 100));
    }

    [Fact]
    public void A_slice_alone_is_a_whole_ring_without_a_gap()
    {
        using var frame = Render(Pie().Replace("gap: 0", "gap: 20"), ("a", 5), ("b", 0), ("c", 0));

        Pixels.AssertNear(SKColors.Red, At(frame, 100, 5));
        Pixels.AssertNear(SKColors.Red, At(frame, 100, 195));
    }

    [Fact]
    public void Track_shows_when_nothing_is_live()
    {
        using var frame = Render(Pie("style: { track: \"#404040\" }"));

        Pixels.AssertNear(Track, At(frame, 100, 20));
        Pixels.AssertNear(Track, At(frame, 100, 180));
    }

    /// A slice's own style goes over the widget's, and the widget's `when` rules go over every slice.
    [Fact]
    public void Slice_styles_layer_between_the_widget_style_and_its_rules()
    {
        const string Donut = """
              - type: donut
                at: [0, 0]
                size: [200, 200]
                hole: 0
                gap: 0
                source: a
                style: { fill: "#FF0000" }
                when:
                  - { above: 50, style: { fill: "#00FF00" } }
                slices:
                  - { source: a }
                  - { source: b, style: { fill: "#0000FF" } }
            """;

        using var normal = Render(Donut, ("a", 10), ("b", 10));
        using var hot = Render(Donut, ("a", 60), ("b", 60));

        Pixels.AssertNear(SKColors.Red, At(normal, 150, 100));
        Pixels.AssertNear(SKColors.Blue, At(normal, 50, 100));
        Pixels.AssertNear(SKColors.Lime, At(hot, 150, 100));
        Pixels.AssertNear(SKColors.Lime, At(hot, 50, 100));
    }

    /// A 3.6° slice can't hold its label, so only the big slice's label is drawn.
    [Fact]
    public void Labels_are_skipped_on_slices_too_thin_to_hold_them()
    {
        using var frame = Render("""
              - type: donut
                at: [0, 0]
                size: [400, 400]
                hole: 0.4
                gap: 0
                labels: inside
                style: { size: 24, color: "#FFFFFF" }
                slices:
                  - { source: a, label: BIG, style: { fill: "#800000" } }
                  - { source: b, label: THIN LABEL, style: { fill: "#000080" } }
            """, ("a", 99), ("b", 1));

        // BIG sits at 6 o'clock on the ring's middle (radius 140); THIN LABEL would sit just left of 12 o'clock.
        Assert.True(Pixels.Count(frame, new SKRectI(160, 320, 240, 360), Pixels.IsBright) > 20);
        Assert.Equal(0, Pixels.Count(frame, new SKRectI(100, 40, 300, 80), Pixels.IsBright));
    }

    /// The label's ring is wide enough and its slice long enough, but its ends would cross the ring's outer edge.
    [Fact]
    public void Inside_labels_that_would_run_past_the_ring_are_skipped()
    {
        static SKImage Donut(string label) => Render($$"""
              - type: donut
                at: [0, 0]
                size: [400, 400]
                hole: 0.6
                gap: 0
                labels: inside
                style: { size: 24, color: "#FFFFFF" }
                slices:
                  - { source: a, label: "{{label}}", style: { fill: "#800000" } }
                  - { source: b, style: { fill: "#000080" } }
            """, ("a", 99), ("b", 1));

        using var fits = Donut("FITS");
        using var tooLong = Donut("THIS LABEL IS FAR TOO LONG");

        // The label would sit at 6 o'clock on the ring's middle (radius 160).
        Assert.True(Pixels.Count(fits, new SKRectI(0, 300, 400, 400), Pixels.IsBright) > 20);
        Assert.Equal(0, Pixels.Count(tooLong, new SKRectI(0, 300, 400, 400), Pixels.IsBright));
    }

    [Fact]
    public void Outside_labels_stay_inside_the_box_across()
    {
        using var frame = Render("""
              - type: donut
                at: [100, 0]
                size: [200, 200]
                labels: outside
                style: { size: 20, color: "#FFFFFF" }
                slices:
                  - { source: a, label: A LONGER LABEL, style: { fill: "#800000" } }
                  - { source: b, label: B LONGER LABEL, style: { fill: "#000080" } }
            """, ("a", 1), ("b", 1));

        Assert.True(Pixels.Count(frame, new SKRectI(100, 0, 300, 200), Pixels.IsBright) > 40);
        Assert.Equal(0, Pixels.Count(frame, new SKRectI(0, 0, 100, 200), Pixels.IsBright));
        Assert.Equal(0, Pixels.Count(frame, new SKRectI(300, 0, 400, 200), Pixels.IsBright));
    }

    /// Two thin slices either side of 12 o'clock: each is long enough for its label, but the labels, both centred near
    /// the top, would overlap, so the second is left out, as a dial's labels are.
    [Fact]
    public void An_outside_label_that_would_overlap_the_one_before_is_left_out()
    {
        using var frame = Render("""
              - type: donut
                at: [0, 0]
                size: [400, 400]
                start: -100
                gap: 0
                labels: outside
                style: { size: 16, color: "#FFFFFF" }
                slices:
                  - { source: a, label: AAAA, style: { fill: "#800000" } }
                  - { source: b, label: BBBB, style: { fill: "#000080" } }
                  - { source: c, style: { fill: "#008000" } }
            """, ("a", 1), ("b", 1), ("c", 34));

        // AAAA is centred about 16 px left of the top's middle (200), and BBBB would be centred as far right.
        Assert.True(Pixels.Count(frame, new SKRectI(160, 0, 200, 24), Pixels.IsBright) > 20);
        Assert.Equal(0, Pixels.Count(frame, new SKRectI(212, 0, 260, 24), Pixels.IsBright));
    }

    [Fact]
    public void Labels_take_the_styles_letter_spacing()
    {
        static int Width(string spacing)
        {
            using var frame = Render($$"""
                  - type: donut
                    at: [0, 0]
                    size: [400, 400]
                    hole: 0.4
                    gap: 0
                    labels: inside
                    style: { size: 24, color: "#FFFFFF", letter_spacing: {{spacing}} }
                    slices:
                      - { source: a, label: BIG, style: { fill: "#800000" } }
                      - { source: b, style: { fill: "#000080" } }
                """, ("a", 99), ("b", 1));
            var (left, right) = (int.MaxValue, int.MinValue);
            for (var x = 100; x < 300; x++)
                if (Pixels.Count(frame, SKRectI.Create(x, 300, 1, 80), Pixels.IsBright) > 0)
                    (left, right) = (Math.Min(left, x), Math.Max(right, x + 1));
            return right - left;
        }

        // "BIG" has two gaps between its letters.
        Assert.InRange(Width("10") - Width("0"), 18, 22);
    }

    [Fact]
    public void Donuts_snapshot()
    {
        using var frame = Frames.Render(Landscape + """
            background:
              - fill: "#0B0F1A"
            widgets:
              - type: donut
                at: [20, 80]
                size: [320, 320]
                hole: 0
                slices:
                  - { source: cpu.power, style: { fill: "#36D1DC" } }
                  - { source: gpu.power, style: { fill: { linear: ["#FF5A36", "#FFB347"] } } }
                  - { source: ram.power, style: { fill: "#A770EF", stroke: { width: 3, color: "#FFFFFF" } } }
              - type: donut
                at: [400, 80]
                size: [320, 320]
                style: { track: "#FFFFFF18", glow: { color: "#36D1DC", radius: 16 } }
                slices:
                  - { source: cpu.power, style: { fill: "#36D1DC" } }
                  - { source: gpu.power, style: { fill: "#FF5A36" } }
                  - { source: ram.power, style: { fill: "#A770EF" } }
              - type: donut
                at: [780, 80]
                size: [320, 320]
                gap: 6
                style: { track: "#FFFFFF18" }
                slices:
                  - { source: cpu.power, style: { fill: "#36D1DC" } }
                  - { source: missing.power, style: { fill: "#FF5A36" } }
                  - { source: ram.power, style: { fill: "#A770EF" } }
              - type: donut
                at: [1160, 80]
                size: [320, 320]
                hole: 0.8
                start: 0
                slices:
                  - { source: cpu.power, style: { fill: "#36D1DC" } }
                  - { source: gpu.power, style: { fill: "#FF5A36" } }
              - type: donut
                at: [1560, 100]
                size: [280, 280]
                hole: 0.3
                gap: 1
                slices:
                  - { source: s.1, style: { fill: "#FF0000" } }
                  - { source: s.2, style: { fill: "#FF8000" } }
                  - { source: s.3, style: { fill: "#FFFF00" } }
                  - { source: s.4, style: { fill: "#80FF00" } }
                  - { source: s.5, style: { fill: "#00FF00" } }
                  - { source: s.6, style: { fill: "#00FF80" } }
                  - { source: s.7, style: { fill: "#00FFFF" } }
                  - { source: s.8, style: { fill: "#0080FF" } }
                  - { source: s.9, style: { fill: "#0000FF" } }
                  - { source: s.10, style: { fill: "#8000FF" } }
                  - { source: s.11, style: { fill: "#FF00FF" } }
                  - { source: s.12, style: { fill: "#FF0080" } }
            """, TestSensors.Of(
            [
                ("cpu.power", 120), ("gpu.power", 280), ("ram.power", 40),
                .. Enumerable.Range(1, 12).Select(i => ($"s.{i}", (object)i)),
            ]));

        Snapshots.Match(frame, "donuts");
    }

    [Fact]
    public void Donut_labels_snapshot()
    {
        using var frame = Frames.Render(Landscape + """
            background:
              - fill: "#0B0F1A"
            widgets:
              - type: donut
                at: [60, 40]
                size: [400, 400]
                hole: 0.45
                labels: inside
                format: "{0:0}%"
                style: { size: 22, weight: bold, color: "#FFFFFF" }
                slices:
                  - { source: cpu.power, label: CPU, style: { fill: "#2A8C94" } }
                  - { source: gpu.power, label: GPU, style: { fill: "#B33A1E" } }
                  - { source: ram.power, label: RAM, style: { fill: "#6B3FA0" } }
                  - { source: fan.power, label: FAN, style: { fill: "#555555" } }
              - type: donut
                at: [700, 40]
                size: [700, 400]
                hole: 0
                labels: outside
                format: "{0:0.0}%"
                style: { size: 24, color: "#9AA4B8" }
                slices:
                  - { source: cpu.power, label: CPU, style: { fill: "#36D1DC" } }
                  - { source: gpu.power, label: GPU, style: { fill: "#FF5A36" } }
                  - { source: ram.power, label: RAM, style: { fill: "#A770EF" } }
                  - { source: fan.power, label: FAN, style: { fill: "#555555" } }
              - type: donut
                at: [1500, 80]
                size: [320, 320]
                labels: outside
                style: { size: 24, color: "#E0E6F0" }
                slices:
                  - { source: cpu.power, label: CPU, style: { fill: "#36D1DC" } }
                  - { source: gpu.power, label: GPU, style: { fill: "#FF5A36" } }
            """, TestSensors.Of(("cpu.power", 120), ("gpu.power", 280), ("ram.power", 40), ("fan.power", 4)));

        Snapshots.Match(frame, "donut-labels");
    }
}
