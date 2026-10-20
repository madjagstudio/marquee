using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock GroupWidget() => new("widget.group", "group", "Group",
        "A box that holds other widgets. Its children are placed from its top-left corner, clipped to it, and take on "
        + $"its text style and letter effects. Groups nest up to {WidgetParser.MaxGroupDepth} deep.",
        [
            new("children", SchemaKind.List, "The widgets inside, drawn in order.") { Required = true, Of = "widget" },
        ]);
}
