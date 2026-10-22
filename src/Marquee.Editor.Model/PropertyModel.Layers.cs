using System.Globalization;
using System.Text.RegularExpressions;

namespace Marquee.Editor.Model;

/// A plain-language section, in the properties panel's fixed order.
public sealed record PlainGroup(PlainSection Section, string Title, IReadOnlyList<PropertyRow> Rows);

public sealed partial class PropertyModel
{
    public IReadOnlyList<PropertyRow> Basics { get; private set; } = [];

    public IReadOnlyList<PlainGroup> MoreStyling { get; private set; } = [];

    /// The existing property and style rows, in their existing order, with nested rows intact.
    public IReadOnlyList<PropertyRow> Raw { get; private set; } = [];

    public string Heading { get; private set; } = "Theme settings";

    static readonly (PlainSection Section, string Title)[] PlainSections =
    [
        (PlainSection.Text, "Text"),
        (PlainSection.FillAndBorder, "Fill and border"),
        (PlainSection.Effects, "Effects"),
        (PlainSection.Position, "Position"),
        (PlainSection.Motion, "Motion"),
    ];

    void BuildLayers()
    {
        Raw = Rows.Concat(Style.SelectMany(section => section.Rows)).ToList();
        // Containers stay intact: their children and list items remain reachable through the same row.
        // Widget warnings keep their dedicated Rules model, as in the existing raw properties panel.
        var basic = new List<PropertyRow>();
        var more = new Dictionary<PlainSection, List<PropertyRow>>();
        foreach (var row in Raw)
        {
            var labels = row.Targets.Select(target => (Owner: PlainOwner(target), Label: Words(target, row.Name))).ToList();
            if (labels.All(entry => entry.Label is { } words &&
                (words.BasicFor.Contains(entry.Owner) || words.Section is PlainSection.Basics or PlainSection.WhatItShows)))
            {
                basic.Add(row);
                continue;
            }

            var section = labels[0].Label?.Section ?? PlainSection.Position;
            if (section is PlainSection.Basics or PlainSection.WhatItShows) section = PlainSection.Position;
            if (!more.TryGetValue(section, out var rows)) more.Add(section, rows = []);
            rows.Add(row);
        }
        Basics = basic;
        MoreStyling = PlainSections.Where(section => more.ContainsKey(section.Section))
            .Select(section => new PlainGroup(section.Section, section.Title, more[section.Section])).ToList();
    }

    string PlainOwner(PropertyTarget target) => Target switch
    {
        PropertyTargetKind.Layer => $"layer.{Type ?? "fill"}", // All layer kinds share the common keys.
        PropertyTargetKind.Canvas => "canvas",
        PropertyTargetKind.Style => "style",
        PropertyTargetKind.Rule => Type == "ring" ? "ring.when" : "when",
        PropertyTargetKind.Ring => "ring",
        _ => target.Widget.Type,
    };

    PlainLabel? Words(PropertyTarget target, string name) => PlainLabels.For(PlainOwner(target), name);

    string WidgetHeading()
    {
        if (Widgets.Count != 1) return $"{Widgets.Count} widgets";
        var widget = Widgets[0];
        string? Text(string key) => _document.TryGetScalar(widget.Path.Child(key), out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : null;
        if (Text("name") is { } name) return name;
        var detail = Text("source") is { } source ? PlainSource(source)
            : Text("text") ?? Text("src") ?? Text("shape");
        return detail is null ? PlainType(widget.Type) : $"{PlainType(widget.Type)}: {detail}";
    }

    static string PlainType(string type) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(type);

    // Expand familiar sensor words for a single key; preserve expressions and custom keys verbatim.
    static string PlainSource(string source)
    {
        if (!Regex.IsMatch(source, @"^(cpu|gpu)\.(temp|load|clock|power|voltage)$", RegexOptions.CultureInvariant)) return source;
        var parts = source.Split('.');
        var reading = parts[1] == "temp" ? "temperature" : parts[1];
        return $"{parts[0].ToUpperInvariant()} {reading}";
    }
}
