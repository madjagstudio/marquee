namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock BarWidget() => new("widget.bar", "bar", "Bar",
        "A bar filled in proportion to a value: `track` paints the empty part, `fill` (or the style's color) the full part.",
        [
            Source("The value that fills the bar: a sensor key or an expression.", required: true),
            .. Range("bar is empty", "bar is full"),
            new("direction", SchemaKind.Enum, "Which way it fills: horizontal fills left to right, vertical bottom to top.")
            {
                Default = "horizontal", Values = ["horizontal", "vertical"],
            },
        ]);
}
