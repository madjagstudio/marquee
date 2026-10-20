namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock TextWidget() => new("widget.text", "text", "Text",
        "Text, which can show values. A line break (`\\n` in double quotes, or a `|` block) starts a new line, and with the "
        + "style's `wrap` a line too wide for the box breaks at spaces. Without a `source`, its `when` rules can only use "
        + "`stale`, which matches while any value in the text is missing.",
        [
            new("text", SchemaKind.Template,
                "The text. {cpu.temp} shows a value, {cpu.temp:0.0} formats it, {gpu.vram_used|bytes} uses a helper and "
                + "{now|time:ddd HH:mm} shows the day and time.")
            {
                Required = true,
            },
        ]);
}
