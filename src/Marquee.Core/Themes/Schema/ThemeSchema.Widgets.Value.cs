namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock ValueWidget() => new("widget.value", "value", "Value",
        "One value as text, laid out as a text widget's is. A missing or stale value shows as —.",
        [
            Source("The value to show: a sensor key such as cpu.temp, or an expression.", required: true),
            new("format", SchemaKind.Format,
                "How to write the value: a .NET format such as \"{0:0.0}°C\", duration, bytes, or time for Unix seconds as "
                + "a local time (\"time:HH:mm\" gives a pattern). Left out, a number shows up to two decimals."),
        ]);
}
