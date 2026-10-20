namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock GaugeWidget() => new("widget.gauge", "gauge", "Gauge",
        "An arc filled in proportion to a value, in the largest square that fits its box. Angles are clockwise from 3 "
        + "o'clock; the default arc opens at the bottom.",
        [
            Source("The value that fills the arc: a sensor key or an expression.", required: true),
            .. Range("arc is empty", "arc is full"),
            new("start", SchemaKind.Number, "Where the arc starts.") { Default = "135", Min = -360, Max = 360, Unit = SchemaUnit.Degrees },
            new("sweep", SchemaKind.Number, "How far the arc runs from its start; negative runs anticlockwise. It can't be 0.")
            {
                Default = "270", Min = -360, Max = 360, Unit = SchemaUnit.Degrees,
            },
            new("thickness", SchemaKind.Number, "How thick the arc is.") { Default = "12", Min = 1, Unit = SchemaUnit.Px },
        ]);
}
