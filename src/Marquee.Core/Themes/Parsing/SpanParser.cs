using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// A text widget's `spans:`, which it takes instead of `text:`, and a value widget's, which it takes instead of `format:`.
/// WidgetParser reads the keys every widget has.
static class SpanParser
{
    // ThemeSchema describes each, and its drift test reads this list.
    internal static readonly string[] Keys = ["text", "style"];

    public const int MinSpans = 1;
    public const int MaxSpans = 32;

    /// The style keys a span may set: how its letters look.
    internal static readonly string[] TextKeys =
    [
        "font", "size", "weight", "color", "shadow", "glow", "letter_spacing",
        "text_stroke", "text_fill", "italic", "underline", "strike", "case",
    ];

    /// The style keys that belong to the widget: its box, where its lines go, and how the whole widget shows. Every
    /// other style key is in TextKeys, which a test checks, so a new style key has to be given to one or the other.
    internal static readonly string[] BoxKeys =
        ["fill", "track", "stroke", "pulse", "opacity", "radius", "padding", "align", "blend", "line_height", "wrap"];

    public static SpansWidget Parse(Fields f, StyleTable styles, ParseErrors errors) => new(Spans(f, styles, errors, withValue: false));

    /// A value widget with `spans:`, in which `{0}` is its value, or with `format:`, or neither; not both.
    public static ValueWidget Value(Fields f, StyleTable styles, ParseErrors errors)
    {
        if (!f.Has("spans")) return new ValueWidget(f.Optional("format") is { } format ? WidgetParser.Format(format) : null);
        if (f.Has("format")) throw Yaml.Error(f.Node, $"{f.What} can only have one of: format, spans");
        var spans = Spans(f, styles, errors, withValue: true);
        // Spans that never show the value are a mistake, as a format without {0} is.
        if (!spans.Any(span => span.Text.ShowsValue))
            throw Yaml.Error(f.Required("spans"), "spans never show the value; include {0} in one of them, e.g. \"{0:0}\"");
        return new ValueWidget(null) { Spans = spans };
    }

    static IReadOnlyList<TextSpan> Spans(Fields f, StyleTable styles, ParseErrors errors, bool withValue) =>
        ListParser.Entries(f.Required("spans"), "spans", MinSpans, MaxSpans, errors, span => Span(span, f.What, styles, withValue));

    /// `widget` names the widget the span is in, such as "value widget", for its errors.
    static TextSpan Span(YamlNode node, string widget, StyleTable styles, bool withValue)
    {
        var f = Yaml.Map(node, "text span", Keys);
        // Read as a value widget's even in a text widget, which has no value, so a `{0}` there is found: in a text widget's
        // `text` it is the number 0, as it always was, but in spans, which are new, it is a value widget's copied.
        var text = WidgetParser.Template(f.Required("text"), withValue: true);
        if (!withValue && text.ShowsValue)
            throw Yaml.Error(f.Required("text"),
                "text: {0} is a value widget's own value, and a text widget has none; use a sensor key, such as {cpu.temp}");
        return new TextSpan(text, f.Optional("style") is { } style ? Style(style, widget, styles) : StyleSpec.Empty);
    }

    /// A box key written in a span's own style is a mistake, and an error, which names every one at once. A named style
    /// may be a widget's as well as a span's, so the box keys it or an `extends` brings in are left out instead, without
    /// a word: the widget's own style has them.
    static StyleSpec Style(YamlNode node, string widget, StyleTable styles)
    {
        if (node is YamlMappingNode map
            && map.Children.Keys.Where(k => k is YamlScalarNode { Value: { } key } && BoxKeys.Contains(key)).ToList() is { Count: > 0 } box)
        {
            var names = box.Select(k => $"'{((YamlScalarNode)k).Value}'").ToList();
            var list = names.Count == 1 ? names[0] : $"{string.Join(", ", names.SkipLast(1))} and {names[^1]}";
            throw Yaml.Error(box[0], $"A span's style can't set {list}: the {widget}'s own style sets {(names.Count == 1 ? "it" : "them")} for all of its spans");
        }

        return styles.Reference(node, "style") with
        {
            Fill = null,
            Track = null,
            Stroke = null,
            Pulse = null,
            Opacity = null,
            Radius = null,
            Padding = null,
            Align = null,
            Blend = null,
            LineHeight = null,
            Wrap = null,
        };
    }
}
