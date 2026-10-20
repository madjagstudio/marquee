namespace Marquee.Core.Themes.Schema;

/// A mapping in layout.yaml and the keys it accepts: the canvas, a style, a `when` rule, one widget type. Every block
/// rejects keys it doesn't list, as the parser does. Id is unique across blocks and unions ("canvas", "style.stroke",
/// "widget.gauge"); Name is the word a theme writes for it (the section's key, a widget's type, a layer's kind key).
public sealed record SchemaBlock(string Id, string Name, string Title, string Help, IReadOnlyList<SchemaKey> Keys)
{
    /// Exactly one of these keys must be there, such as a rule's condition.
    public IReadOnlyList<string> ExactlyOneOf { get; init; } = [];

    /// At least one of these keys must be there, such as what a ring rule changes.
    public IReadOnlyList<string> AtLeastOneOf { get; init; } = [];

    /// For a widget type: a small widget of the type, as layout.yaml writes it, which the theme editor starts a new one
    /// from: its type, its size, the keys it needs and a sensible source, at the top-left corner. Null for other blocks.
    public string? Example { get; init; }

    public SchemaKey? Key(string name) => Keys.FirstOrDefault(k => k.Name == name);
}

/// Something that takes one of several forms, each a block of its own: a widget, chosen by its `type`, or a background
/// layer or gradient, chosen by which of the variants' own keys it has (`image:`, `linear:`). Every variant also takes
/// Common's keys. A variant may list a common key again to change it, as a value widget makes `source` required.
public sealed record SchemaUnion(string Id, string Title, string Help, SchemaBlock Common, IReadOnlyList<SchemaBlock> Variants)
{
    /// The key that names the variant, such as a widget's `type`; null when a variant is chosen by the key named after
    /// it, which is then the first of its own keys.
    public string? Discriminator { get; init; }

    /// Help for the discriminator key.
    public string DiscriminatorHelp { get; init; } = "";

    public IEnumerable<string> Names => Variants.Select(v => v.Name);

    public SchemaBlock? Variant(string name) => Variants.FirstOrDefault(v => v.Name == name);

    /// The discriminator, as an enum of the variants' names; null for a union without one.
    public SchemaKey? DiscriminatorKey => Discriminator is { } key
        ? new SchemaKey(key, SchemaKind.Enum, DiscriminatorHelp) { Required = true, Values = Names.ToList() }
        : null;

    /// Every key one variant accepts, in the order a theme usually writes them: the discriminator, the common keys (as
    /// the variant changes them) and the variant's own; or, without a discriminator, the variant's own keys first.
    public IReadOnlyList<SchemaKey> KeysOf(SchemaBlock variant)
    {
        var common = Common.Keys.Select(key => variant.Key(key.Name) ?? key);
        var own = variant.Keys.Where(key => Common.Key(key.Name) is null);
        return DiscriminatorKey is { } discriminator ? [discriminator, .. common, .. own] : [.. own, .. common];
    }
}
