using Marquee.Core.Sensors;

namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock GraphWidget() => new("widget.graph", "graph", "Graph",
        "A line of a sensor's recent values, newest at the right. The stroke draws the line, `fill` the area under it and "
        + "`track` the background.",
        [
            new("source", SchemaKind.SensorKey, "The sensor whose history is drawn: one key, such as gpu.load, not an expression.")
            {
                Required = true,
            },
            new("min", SchemaKind.Number, "The value at the bottom. Left out, the scale fits the values shown."),
            new("max", SchemaKind.Number, "The value at the top; more than min if both are set. Left out, the scale fits the values shown."),
            new("points", SchemaKind.Integer, "How many recent values span the width, one per sensor update.")
            {
                Default = "60", Min = 2, Max = SensorStore.DefaultHistoryLength,
            },
        ]);
}
