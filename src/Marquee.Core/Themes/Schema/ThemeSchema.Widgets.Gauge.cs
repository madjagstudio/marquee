namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock GaugeWidget() => new("widget.gauge", "gauge", "Gauge",
        "An arc filled in proportion to a value, in the largest square that fits its box. Angles are clockwise from 3 "
        + "o'clock; the default arc opens at the bottom.",
        [
            Source("The value that fills the arc: a sensor key or an expression.", required: true),
            .. Range("arc is empty", "arc is full"),
            new("start", SchemaKind.Number, "Where the arc starts.")
            {
                Default = Number(Arcs.DefaultStart), Min = -Arcs.MaxAngle, Max = Arcs.MaxAngle, Unit = SchemaUnit.Degrees,
            },
            new("sweep", SchemaKind.Number, "How far the arc runs from its start; negative runs anticlockwise. It can't be 0.")
            {
                Default = Number(Arcs.DefaultSweep), Min = -Arcs.MaxAngle, Max = Arcs.MaxAngle, NotZero = true, Unit = SchemaUnit.Degrees,
            },
            new("thickness", SchemaKind.Number, "How thick the arc is; 0 draws no arc, for a dial with only a needle.")
            {
                Default = Number(Arcs.DefaultThickness), Min = 0, Unit = SchemaUnit.Px,
            },
            .. Segments("arc", SchemaUnit.Degrees, SegmentSpec.DefaultGaugeGap,
                "Each segment must keep at least 1°, and a full ring has a gap at the join too."),
            new("needle", SchemaKind.Block, "A needle pointing at the value, which makes the gauge a dial.") { Of = "gauge.needle" },
            new("ticks", SchemaKind.Block, "Tick marks inside the arc, and the values of the major ones.") { Of = "gauge.ticks" },
        ]);
}
