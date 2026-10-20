using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

/// Reading and writing a `style:` as the file holds it, for a widget or a list item: a named style (`style: meter`), a
/// style written in place (`style: { size: 30 }`), or one in place over a named one (`style: { extends: meter, size:
/// 30 }`). Named styles are read here, never written: editing them is E2.
///
/// A style flattens as the parser builds it (StyleTable): `extends` first, then the keys set over it, a stroke,
/// shadow, glow, pulse or text stroke merged key by key, and anything else replaced whole.
static class StyleValues
{
    static readonly YamlPath Styles = YamlPath.Root.Child("styles");

    /// The names in `styles:`, in the file's order.
    public static IReadOnlyList<string> Names(LayoutDocument document) =>
        document.GetNode(Styles) is { Kind: LayoutNodeKind.Mapping } styles ? styles.Keys : [];

    /// The named style the style at `style` starts from: its name, or its `extends:`. Null for none.
    public static string? NameOf(LayoutDocument document, YamlPath style) =>
        document.TryGetScalar(style, out var name) && name.Length > 0 ? name
        : document.TryGetScalar(style.Child("extends"), out var extends) && extends.Length > 0 ? extends
        : null;

    /// The keys the style at `style` writes in place, without `extends`; empty for a named style or none.
    public static YamlMap Own(LayoutDocument document, YamlPath style) =>
        Read(document, style) is YamlMap map ? map.Without("extends") : YamlMap.Empty;

    /// The named style `name`, flattened through its `extends` chain. Empty for a style the file hasn't, and a chain
    /// that comes back to itself stops there (the loader refuses such a theme, but the file may be mid-edit).
    public static YamlMap Named(LayoutDocument document, string? name) => Named(document, name, []);

    /// The style at `style` as it draws: its named style, then what it sets in place.
    public static YamlMap Effective(LayoutDocument document, YamlPath style) =>
        Merge(Named(document, NameOf(document, style)), Own(document, style));

    /// The style keys a group passes down to its children, as the renderer draws them (StyleSpec.Inherited): its text
    /// keys and letter effects. Its box keys (fill, stroke, radius, ...) stay with the group.
    public static IReadOnlyList<string> PassedDown { get; } =
        ["font", "size", "weight", "color", "align", "letter_spacing", "line_height", "wrap", "text_stroke", "text_fill", "italic",
            "underline", "strike", "case"];

    /// What the groups round the widget at `widget` pass down to it: each group's effective style over what its own
    /// groups pass it, outermost first, kept to PassedDown. Empty for a top-level widget.
    public static YamlMap FromGroups(LayoutDocument document, YamlPath widget)
    {
        var groups = new List<YamlPath>();
        for (var group = CanvasGeometry.GroupOf(widget); group is not null; group = CanvasGeometry.GroupOf(group)) groups.Insert(0, group);
        var passed = YamlMap.Empty;
        foreach (var group in groups)
        {
            var drawn = Merge(passed, Effective(document, group.Child("style")));
            passed = new YamlMap(drawn.Entries.Where(e => PassedDown.Contains(e.Key)).ToList());
        }

        return passed;
    }

    /// The style the widget at `widget` draws with, before its `when` rules: what its groups pass down, then its own
    /// effective style over that.
    public static YamlMap Drawn(LayoutDocument document, YamlPath widget) =>
        Merge(FromGroups(document, widget), Effective(document, widget.Child("style")));

    /// `over` written over `under`: a style block (stroke, shadow, ...) merged key by key, anything else replaced.
    public static YamlMap Merge(YamlMap under, YamlMap over)
    {
        var merged = under;
        foreach (var (key, value) in over.Entries)
        {
            merged = IsBlock(key) && merged[key] is YamlMap below && value is YamlMap above
                ? merged.With(key, Merge(below, above))
                : merged.With(key, value);
        }

        return merged;
    }

    /// The value at `key`'s steps inside a style (`stroke`, `width`), or null.
    public static YamlValue? Get(YamlMap style, IReadOnlyList<string> key)
    {
        YamlValue? value = style;
        foreach (var step in key) value = (value as YamlMap)?[step];
        return value;
    }

    /// The style keys a style map sets, as rows name them: `fill`, `stroke.width`.
    public static IReadOnlyList<string> Leaves(YamlMap style) =>
        style.Entries.SelectMany(e => IsBlock(e.Key) && e.Value is YamlMap block && block.Entries.Count > 0
            ? block.Entries.Select(inner => $"{e.Key}.{inner.Key}")
            : [e.Key]).ToList();

    /// True for a style key that holds a block of its own keys, which merges key by key.
    public static bool IsBlock(string key) => ThemeSchema.Style.Key(key)?.Kind == SchemaKind.Block;

    /// Sets each key (its steps inside the style, and its value) on the style at `style`, keeping what else it sets.
    /// A named style becomes `{ extends: name, … }` in one edit, so other widgets using the name don't change; a widget
    /// without a style gets one in place.
    public static LayoutDocument Set(LayoutDocument document, YamlPath style, IReadOnlyList<(IReadOnlyList<string> Key, YamlValue Value)> writes)
    {
        if (writes.Count == 0) return document;
        if (document.GetNode(style) is { Kind: LayoutNodeKind.Mapping })
        {
            foreach (var (key, value) in writes) document = EditDocument.Set(document, At(style, key), value);
            return document;
        }

        // No style, an empty one or a name: the whole style is written at once.
        var map = NameOf(document, style) is { } name ? YamlMap.Empty.With("extends", name) : YamlMap.Empty;
        foreach (var (key, value) in writes) map = With(map, key, value);
        return EditDocument.Set(document, style, map);
    }

    /// Removes a key set in place, then a style block it leaves empty, and then a style left with nothing but its
    /// named style goes back to `style: name`, and one left with nothing at all goes.
    public static LayoutDocument Remove(LayoutDocument document, YamlPath style, IReadOnlyList<string> key)
    {
        var path = At(style, key);
        if (document.GetNode(style) is not { Kind: LayoutNodeKind.Mapping } || !document.Exists(path)) return document;

        document = document.RemoveKey(path);
        for (var parent = path.Parent!; !parent.Equals(style); parent = parent.Parent!)
        {
            if (document.GetNode(parent) is not { Kind: LayoutNodeKind.Mapping, Count: 0 }) break;
            document = document.RemoveKey(parent);
        }

        return Collapse(document, style);
    }

    /// The style back to its named style alone (`style: meter`), or gone when it has none.
    public static LayoutDocument Clear(LayoutDocument document, YamlPath style) =>
        document.GetNode(style) is not { Kind: LayoutNodeKind.Mapping or LayoutNodeKind.Empty } ? document
        : NameOf(document, style) is { } name ? EditDocument.Set(document, style, name)
        : document.RemoveKey(style);

    /// Makes the style start from the named style `name`, keeping what it sets in place; null leaves it with only what
    /// it sets in place.
    public static LayoutDocument Pick(LayoutDocument document, YamlPath style, string? name)
    {
        var node = document.GetNode(style);
        if (node is { Kind: LayoutNodeKind.Mapping })
        {
            return name is not null
                ? EditDocument.Set(document, style.Child("extends"), name)
                : Collapse(EditDocument.Remove(document, style.Child("extends")), style);
        }

        return name is not null ? EditDocument.Set(document, style, name) : EditDocument.Remove(document, style);
    }

    /// The path of a key's steps inside the style at `style`.
    public static YamlPath At(YamlPath style, IReadOnlyList<string> key) => key.Aggregate(style, (path, step) => path.Child(step));

    /// A style mapping left empty goes, and one left with only `extends` becomes the name.
    static LayoutDocument Collapse(LayoutDocument document, YamlPath style) => document.GetNode(style) switch
    {
        { Kind: LayoutNodeKind.Mapping, Count: 0 } => document.RemoveKey(style),
        { Kind: LayoutNodeKind.Mapping, Count: 1, Keys: ["extends"] } when NameOf(document, style) is { } name =>
            EditDocument.Set(document, style, name),
        _ => document,
    };

    static YamlMap With(YamlMap map, IReadOnlyList<string> key, YamlValue value) =>
        key.Count == 1
            ? map.With(key[0], value)
            : map.With(key[0], With(map[key[0]] as YamlMap ?? YamlMap.Empty, key.Skip(1).ToList(), value));

    static YamlValue? Read(LayoutDocument document, YamlPath path)
    {
        try
        {
            return document.ValueAt(path);
        }
        catch (LayoutEditException)
        {
            // A style that repeats itself through an alias can't be shown; the panel shows it as not set.
            return null;
        }
    }

    static YamlMap Named(LayoutDocument document, string? name, HashSet<string> seen)
    {
        if (name is null || !seen.Add(name) || Read(document, Styles.Child(name)) is not YamlMap own) return YamlMap.Empty;
        var under = own["extends"] is YamlText { Value: var parent } ? Named(document, parent, seen) : YamlMap.Empty;
        return Merge(under, own.Without("extends"));
    }
}
