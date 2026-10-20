using Marquee.Core.Themes.Parsing;

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
                + "{now|time:ddd HH:mm} shows the day and time."),
            new("spans", SchemaKind.List,
                "Instead of `text`: runs of text, each in its own style, side by side on one baseline, such as a big number "
                + "with a small unit. The widget's style places them as it places text.")
            {
                Of = "text.span", MinItems = SpanParser.MinSpans, MaxItems = SpanParser.MaxSpans,
            },
        ])
    {
        ExactlyOneOf = ["text", "spans"],
    };

    static SchemaBlock TextSpanBlock() => new("text.span", "spans", "Text spans",
        "One run of a text or value widget's `spans`. Its style goes over the widget's, and the widget's `when` rules go over "
        + "every span, so a rule that turns the text red turns every span red. A line is as tall as its largest span, and "
        + "its spans share the baseline of the tallest.",
        [
            new("text", SchemaKind.Template,
                "The span's text, a template as a text widget's is, where a value widget's {0} is its value. A line break in "
                + "it starts a new line.")
            {
                Required = true,
            },
            new("style", SchemaKind.Style,
                "The span's letters: a style's name, or a style written in place, with the text keys only. The box "
                + "keys, such as `fill`, `padding`, `align` and `wrap`, are the widget's: written in place they are an "
                + "error, and a named style's are left out without a warning.")
            {
                StyleKeys = StyleBlock().Keys.Select(k => k.Name)
                    .Where(name => name == "extends" || SpanParser.TextKeys.Contains(name)).ToList(),
            },
        ]);
}
