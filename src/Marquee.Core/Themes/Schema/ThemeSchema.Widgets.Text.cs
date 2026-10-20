namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock TextWidget() => new("widget.text", "text", "Text",
        "Text on one line, which can show values. Without a `source`, its `when` rules can only use `stale`, which "
        + "matches while any value in the text is missing.",
        [
            new("text", SchemaKind.Template,
                "The text. {cpu.temp} shows a value, {cpu.temp:0.0} formats it and {gpu.vram_used|bytes} uses a helper.")
            {
                Required = true,
            },
        ]);
}
