using System.Globalization;
using System.Text;
using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// What the layers panel calls a widget that has no `name:`: its type and what it shows, as the file writes it, such as
/// "Gauge · cpu.temp", "Text · CPU {cpu.load:0}%" or "Image · images/art.png"; a group is just "Group".
static class WidgetNames
{
    /// Longest the shown part runs before it is cut, at a word where it can be.
    const int MaxDetail = 32;

    public static string Of(LayoutDocument document, YamlPath widget, string type)
    {
        var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(type);
        return Detail(document, widget, type) is { Length: > 0 } detail ? $"{title} · {Shortened(detail)}" : title;
    }

    /// What the widget shows: its source, else its text, file, shape, or first series' or slice's source.
    static string? Detail(LayoutDocument document, YamlPath widget, string type)
    {
        if (Text(document, widget.Child("source")) is { } source) return source;
        return type switch
        {
            "text" => Text(document, widget.Child("text")) ?? Spans(document, widget.Child("spans")),
            "image" or "video" => Text(document, widget.Child("src")),
            "shape" => Text(document, widget.Child("shape")),
            "graph" => Text(document, widget.Child("series").Item(0).Child("source")),
            "donut" => Text(document, widget.Child("slices").Item(0).Child("source")),
            _ => null,
        };
    }

    /// The spans' texts run together, as they read on screen.
    static string? Spans(LayoutDocument document, YamlPath spans)
    {
        var text = new StringBuilder();
        for (var i = 0; Text(document, spans.Item(i).Child("text")) is { } span; i++) text.Append(span);
        return text.Length > 0 ? text.ToString() : null;
    }

    /// The single value at `path`; null for none, or for a list or mapping.
    static string? Text(LayoutDocument document, YamlPath path) => document.TryGetScalar(path, out var value) ? value : null;

    /// On one line, with runs of spaces as one, and cut at a word boundary with "…" when it runs long.
    static string Shortened(string detail)
    {
        var line = string.Join(' ', detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= MaxDetail) return line;
        var cut = line.LastIndexOf(' ', MaxDetail);
        return line[..(cut > MaxDetail / 2 ? cut : MaxDetail)].TrimEnd() + "…";
    }
}
