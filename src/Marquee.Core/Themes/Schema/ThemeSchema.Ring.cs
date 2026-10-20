namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static string[] RingEffects => ["static", "breathe", "spin", "rainbow"];

    const string EffectHelp = "static is the color on every LED, breathe fades it in and out, spin sends a bright "
        + "segment round the ring, and rainbow turns the hues round it, ignoring color.";

    /// The keys the ring sets and its rules change. A rule's have no defaults: it leaves what it doesn't set as it was.
    static SchemaKey[] RingFields(bool withDefaults) =>
    [
        new("color", SchemaKind.Color, "The LEDs' color. An alpha byte dims it.") { Default = withDefaults ? "#FFFFFF" : null },
        new("effect", SchemaKind.Enum, EffectHelp) { Default = withDefaults ? "static" : null, Values = RingEffects },
        new("speed", SchemaKind.Number, "How fast the effect runs: 2 is twice as fast.")
        {
            Default = withDefaults ? "1" : null, Min = 0.1, Max = 10, Unit = SchemaUnit.Multiplier,
        },
        new("brightness", SchemaKind.Number, "How bright the LEDs are.")
        {
            Default = withDefaults ? "100" : null, Min = 0, Max = 100, Unit = SchemaUnit.Percent,
        },
    ];

    static SchemaBlock RingBlock() => new("ring", "ring", "LED ring",
        "What the screen's ring of 60 LEDs shows. A key left out takes the default below, so `ring: { color: \"#3A7BFF\" }` "
        + "shows that blue, static, at full brightness. `ring:` in config.yaml overrides it.",
        [
            .. RingFields(withDefaults: true),
            Source("What the ring's `when` rules compare: a sensor key or an expression."),
            new("when", SchemaKind.List, "Changes to make while a condition holds. Every matching rule applies, in order.")
            {
                Of = "ring.when",
            },
        ]);

    static SchemaBlock RingWhenBlock() => new("ring.when", "when", "Ring when rules",
        "Each rule has one condition, which compares the ring's `source`, and changes at least one of color, effect, "
        + "speed and brightness while it holds. A later rule wins for each key it sets.",
        [
            .. Conditions("ring"),
            .. RingFields(withDefaults: false),
        ])
    {
        ExactlyOneOf = ConditionNames,
        AtLeastOneOf = ["color", "effect", "speed", "brightness"],
    };
}
