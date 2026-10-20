using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock ValueWidget() => new("widget.value", "value", "Value",
        "One value as text, laid out as a text widget's is. A missing or stale value shows as —. A value with a smaller "
        + "unit, such as a big 52 with a small °C, takes `spans` instead of `format`.",
        [
            Source("The value to show: a sensor key such as cpu.temp, or an expression.", required: true),
            new("format", SchemaKind.Format,
                "How to write the value: a .NET format such as \"{0:0.0}°C\", duration, bytes, or time for Unix seconds as "
                + "a local time (\"time:HH:mm\" gives a pattern). Left out, a number shows up to two decimals."),
            new("spans", SchemaKind.List,
                "Instead of `format`: runs of text, each in its own style, side by side on one baseline, as a text widget's "
                + "spans are. {0} in them is the value after `smooth`, and {0:0.0} formats it. A missing or stale value "
                + "shows a single — in the first span's style.")
            {
                Of = "text.span", MinItems = SpanParser.MinSpans, MaxItems = SpanParser.MaxSpans,
            },
        ])
    {
        AtMostOneOf = ["format", "spans"],
    };
}
