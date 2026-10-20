namespace Marquee.Core.Themes.Schema;

/// What a key's value is. ThemeSchema.KindHelp says how each is written in layout.yaml.
public enum SchemaKind
{
    Number,
    Integer,
    Boolean,

    /// Any single value, read as text: `equals: 3` and `equals: idle` both work.
    String,

    /// One of SchemaKey.Values.
    Enum,
    Color,

    /// A color or a gradient (ThemeSchema.Gradient).
    Paint,
    FontFamily,

    /// 100 to 900 (SchemaKey.Min and Max), or one of the names in SchemaKey.Values.
    FontWeight,

    /// A sensor key or an expression over sensor keys, such as `cpu.temp` or `max(cpu.load, gpu.load)`.
    Expression,

    /// A single sensor key, not an expression.
    SensorKey,

    /// Text with `{key}` placeholders.
    Template,

    /// A value widget's format: a .NET composite format or a helper name.
    Format,

    /// A file in the theme folder. SchemaKey.FileTypes lists the extensions the parser accepts, when it checks them.
    ThemeFile,

    /// `[x, y]` in px.
    Point,

    /// `[width, height]` in px, each at least 1, or at least SchemaKey.Min when that is set.
    Size,

    /// The name of a style in `styles:`.
    StyleName,

    /// A style name, or a style written in place.
    Style,

    /// A mapping described by the block or union SchemaKey.Of names.
    Block,

    /// A list of SchemaKey.Of's blocks, or of SchemaKey.ItemKind values.
    List,

    /// A mapping of names, chosen by the theme, to SchemaKey.Of's blocks.
    Map,

    /// A shader layer's uniforms: names mapped to an expression or a list of expressions.
    Uniforms,
}

/// The unit of a number.
public enum SchemaUnit { None, Px, Degrees, Ms, Fraction, Percent, Multiplier }

/// One key of a block: what it takes, whether it must be there, what it is when left out, and what it does.
public sealed record SchemaKey(string Name, SchemaKind Kind, string Help)
{
    public bool Required { get; init; }

    /// The value used when the key is left out, written as in layout.yaml (`135`, `contain`, `[0.5, 0.5]`, `"#FFFFFF"`
    /// without the quotes). Null when leaving it out means the feature is off, or when Help says what happens instead.
    public string? Default { get; init; }

    /// A style key's default where one widget type's painter falls back to something else, by widget type, written as
    /// Default is: a graph's stroke is 2 px wide and round. StyleDefaults holds each as a constant.
    public IReadOnlyDictionary<string, string> DefaultFor { get; init; } = new Dictionary<string, string>();

    /// For a style key: the widget types that draw with it (a group also counts the text keys it passes down), in
    /// ThemeSchema.Widgets' order; empty when every widget does. A stroke's `cap` names fewer types than `stroke`, as only
    /// a line's ends have one. Empty for keys outside a style. ThemeSchema.WidgetStyles is the table it comes from.
    public IReadOnlyList<string> AppliesTo { get; init; } = [];

    /// The range of a number or integer, or of each number of a size or a list of numbers, inclusive; null where it has
    /// no bound.
    public double? Min { get; init; }
    public double? Max { get; init; }

    /// A lower Min in one form of the block, such as 0 for a line shape's size: the key that chooses the form, the value
    /// it must have, and the Min there.
    public (string Key, string Value, double Min)? MinFor { get; init; }

    public SchemaUnit Unit { get; init; }

    /// An enum's values, or the names a font weight also takes, in the order the parser lists them.
    public IReadOnlyList<string> Values { get; init; } = [];

    /// The id of the block or union a Block, List or Map key holds (ThemeSchema.Block or ThemeSchema.Union).
    public string? Of { get; init; }

    /// What a List of plain values holds, such as Color for a gradient's colors.
    public SchemaKind? ItemKind { get; init; }

    /// How many items a List, or entries a Map or Uniforms, may have; null where there is no limit.
    public int? MinItems { get; init; }
    public int? MaxItems { get; init; }

    /// How many characters a String may have; null where there is no limit.
    public int? MaxLength { get; init; }

    /// The extensions a ThemeFile must have, lower case with the dot; empty when the parser takes any file.
    public IReadOnlyList<string> FileTypes { get; init; } = [];

    /// Set when the key belongs to one form of its block, such as `start` to `shape: arc`: the key that chooses the form
    /// and the value it must have. The parser refuses the key with any other value.
    public (string Key, string Value)? OnlyFor { get; init; }

    /// The key also takes false, which turns its feature off, as a gauge's tick `labels` do.
    public bool TakesFalse { get; init; }

    /// For a Style key that may set only some of a style's keys, such as a span's: the keys it may set, in
    /// ThemeSchema.Style's order. Empty when it may set any.
    public IReadOnlyList<string> StyleKeys { get; init; } = [];
}
