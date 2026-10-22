using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

/// One row of the properties panel: a catalog key of the selected widgets, what it holds in the file, and what the panel
/// needs to draw its control.
/// - Name: where it is, from the widget: `size`, `needle.width`, `style.stroke.width`, `series[1].style.fill`.
/// - Key: the catalog's key, with its kind, unit, help, choices, OnlyFor, MinFor, TakesFalse, MaxLength and StyleKeys.
/// - Min, Max and Step: the slider's range, with a MinFor's lower bound when the widget is in that form.
/// - Default: what the key is when left out, for this widget type (DefaultFor), as layout.yaml writes it; null when
///   leaving it out turns the feature off, or when the selected types' defaults differ.
/// - Value and IsSet: the value written in the file, the same on every selected widget; null with IsSet false when no
///   widget sets it. IsMixed is true when the widgets differ: other values, or set on some and not the others.
/// - Inherited: a style key's value from the widget's named style (`style: meter`), or a text key its groups pass down
///   to it, when the widget doesn't set it.
/// - IsEditable: false for `type`, and for a key in or under an anchor or alias, which the file must be edited for.
/// - Choices: an enum's values, a font weight's names, or the theme's style names for a style.
/// - Children: a block's rows (stroke, needle, ticks, grid) or a style's, written in place in a list item.
/// - Items: a list's items (series, slices, spans), each with its own rows.
public sealed record PropertyRow(string Name, SchemaKey Key)
{
    public SchemaKind Kind => Key.Kind;

    public SchemaUnit Unit => Key.Unit;

    public string Help => Key.Help;

    public string PlainLabel { get; init; } = Key.Name;

    public string Tooltip { get; init; } = Key.Help;

    public double? Min { get; init; }

    public double? Max { get; init; }

    public double Step { get; init; } = 1;

    public string? Default { get; init; }

    public YamlValue? Value { get; init; }

    public bool IsSet { get; init; }

    public bool IsMixed { get; init; }

    public YamlValue? Inherited { get; init; }

    public bool IsEditable { get; init; }

    public IReadOnlyList<string> Choices { get; init; } = [];

    public IReadOnlyList<PropertyRow> Children { get; init; } = [];

    public IReadOnlyList<PropertyItem> Items { get; init; } = [];

    /// A list can take another item: it is under its MaxItems.
    public bool CanAdd => Kind == SchemaKind.List && IsEditable && (Key.MaxItems is not { } max || Items.Count < max);

    /// A list can lose an item: it is over its MinItems.
    public bool CanRemove => Kind == SchemaKind.List && IsEditable && Items.Count > (Key.MinItems ?? 0);

    /// The key can be left out, so Reset is offered (where a widget sets it: IsSet or IsMixed): false for a key the
    /// theme requires (`at`, `size`, a form's key such as `shape`, an item's `source`), for `type`, and for the key
    /// written of an exactly-one-of (a text's `text` or `spans`), which the theme can't do without either.
    public bool CanReset => IsEditable && !Key.Required && Key.Name != "type"
        && !Targets.Any(target => target.Groups?.ExactlyOneOf.Contains(Key.Name) == true);

    /// Where the row writes, one path per selected widget.
    public IReadOnlyList<YamlPath> Paths => Targets.Select(target => target.Path).ToList();

    internal IReadOnlyList<PropertyTarget> Targets { get; init; } = [];

    /// This row or one of its children or items' rows, by Name; null when there is none.
    public PropertyRow? Find(string name) =>
        Name == name ? this : Children.Concat(Items.SelectMany(item => item.Rows)).Select(row => row.Find(name)).FirstOrDefault(row => row is not null);
}

/// One item of a list row: its index, its path and its rows.
public sealed record PropertyItem(int Index, YamlPath Path, IReadOnlyList<PropertyRow> Rows);

/// A titled group of style rows: Text, Colour and fill, Outline, Effects.
public sealed record PropertySection(string Title, IReadOnlyList<PropertyRow> Rows);

/// Where one widget's row writes.
/// - Path: the value's path, written in place: for a style key, inside the widget's inline style.
/// - Key: the catalog's key for this widget's type, which its value is checked against.
/// - Style and StyleKey: for a style key, the `style` the widget or item has, and the key's steps inside it
///   (`stroke`, `width`), so a named style can be rewritten as overrides.
/// - Block: the mapping the key is in; Siblings, the keys that mapping takes, for OnlyFor and MinFor; Groups, the
///   catalog block whose ExactlyOneOf and AtMostOneOf the key may be in.
internal sealed record PropertyTarget(WidgetRef Widget, YamlPath Path, SchemaKey Key, YamlPath Block)
{
    public IReadOnlyList<SchemaKey> Siblings { get; init; } = [];

    public SchemaBlock? Groups { get; init; }

    public YamlPath? Style { get; init; }

    public IReadOnlyList<string> StyleKey { get; init; } = [];
}

/// How a widget's style is written: the named style it starts from (`style: meter`, or `extends:` in place), and the
/// keys it sets over it, as rows name them inside the style (`fill`, `stroke.width`).
public sealed record StyleUse(string? Name, IReadOnlyList<string> Overrides)
{
    public int Count => Overrides.Count;

    /// "meter", "meter + 1 override", "meter + 3 overrides"; without a named style, how many keys it sets.
    public string Label => (Name, Count) switch
    {
        (null, 0) => "No style",
        (null, 1) => "1 key set here",
        (null, _) => $"{Count} keys set here",
        (_, 0) => Name,
        (_, 1) => $"{Name} + 1 override",
        _ => $"{Name} + {Count} overrides",
    };
}

/// A value the properties panel won't write, with why: out of range, the wrong kind, too long.
public sealed class PropertyValueException(string reason) : Exception(reason)
{
    public string Reason => Message;
}
