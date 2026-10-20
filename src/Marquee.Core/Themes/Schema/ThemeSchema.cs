namespace Marquee.Core.Themes.Schema;

/// Every key layout.yaml can have: where it goes, what it takes, its default and one line of help. It describes the
/// parser (Themes/Parsing) rather than driving it, and ThemeSchemaDriftTests checks that the two agree, block by block.
/// The editor builds its panels from it, and tools/Marquee.SchemaDocs writes docs/themes-reference.md and
/// docs/layout.schema.json from it.
///
/// Each area's blocks are built in a file of their own (ThemeSchema.Widgets.Gauge.cs, ThemeSchema.Styles.cs, ...), by
/// methods that read nothing but constants. The properties here call them, so the order C# runs static initializers in
/// across partial files doesn't matter, and the index over them is built on first use.
public static partial class ThemeSchema
{
    /// The top level of layout.yaml.
    public static SchemaBlock Layout { get; } = LayoutBlock();

    public static SchemaBlock Canvas { get; } = CanvasBlock();

    /// A style and its blocks, each key saying which widget types use it (ThemeSchema.Widgets.Editor.cs).
    public static SchemaBlock Style { get; } = WithUse(StyleBlock());
    public static SchemaBlock Stroke { get; } = WithUse(StrokeBlock());
    public static SchemaBlock Shadow { get; } = WithUse(ShadowBlock());
    public static SchemaBlock Glow { get; } = WithUse(GlowBlock());
    public static SchemaBlock Pulse { get; } = WithUse(PulseBlock());
    public static SchemaBlock TextStroke { get; } = WithUse(TextStrokeBlock());

    /// The mapping form of a paint: a linear or radial gradient.
    public static SchemaUnion Gradient { get; } = GradientUnion();

    /// The entries of `background:`.
    public static SchemaUnion Background { get; } = BackgroundUnion();

    /// The entries of `widgets:` and of a group's `children:`, by `type`, each with the example a new one starts as.
    public static SchemaUnion Widgets { get; } = WithExamples(WidgetUnion());

    /// A widget's `when` rule.
    public static SchemaBlock When { get; } = WhenBlock();

    public static SchemaBlock Ring { get; } = RingBlock();

    /// A `when` rule of the ring.
    public static SchemaBlock RingWhen { get; } = RingWhenBlock();

    /// A gauge's `needle:` and `ticks:`, which make it a dial.
    public static SchemaBlock Needle { get; } = NeedleBlock();
    public static SchemaBlock Ticks { get; } = TicksBlock();

    /// A graph's gridlines, and each entry of its `series:`.
    public static SchemaBlock GraphGrid { get; } = GraphGridBlock();
    public static SchemaBlock GraphSeries { get; } = GraphSeriesBlock();

    /// An entry of a donut widget's `slices:`.
    public static SchemaBlock DonutSlice { get; } = DonutSliceBlock();

    /// The blocks that aren't part of a union, in the order the reference describes them.
    public static IReadOnlyList<SchemaBlock> Blocks =>
        [Layout, Canvas, Style, Stroke, Shadow, Glow, Pulse, TextStroke, When, Ring, RingWhen, Needle, Ticks, GraphGrid, GraphSeries, DonutSlice];

    public static IReadOnlyList<SchemaUnion> Unions => [Gradient, Background, Widgets];

    static readonly Lazy<Dictionary<string, SchemaBlock>> BlocksById = new(() =>
        Blocks.Concat(Unions.SelectMany(u => u.Variants.Prepend(u.Common))).ToDictionary(b => b.Id, StringComparer.Ordinal));

    /// Any block by id: a plain block, a union's common keys ("widget.common") or one of its variants ("widget.gauge").
    public static SchemaBlock? Block(string id) => BlocksById.Value.GetValueOrDefault(id);

    public static SchemaUnion? Union(string id) => Unions.FirstOrDefault(u => u.Id == id);

    /// One widget type's own keys; WidgetKeys has every key it accepts.
    public static SchemaBlock? Widget(string type) => Widgets.Variant(type);

    /// Every key a widget of this type accepts, `type` and the common keys included.
    public static IReadOnlyList<SchemaKey> WidgetKeys(string type) =>
        Widget(type) is { } variant ? Widgets.KeysOf(variant) : throw new ArgumentException($"There is no '{type}' widget.", nameof(type));
}
