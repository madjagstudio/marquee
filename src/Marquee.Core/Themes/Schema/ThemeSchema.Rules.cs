namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    /// The conditions a widget's rule and the ring's rules share.
    static SchemaKey[] Conditions(string owner) =>
    [
        new("above", SchemaKind.Number, $"Matches while the {owner}'s source is above this number."),
        new("below", SchemaKind.Number, $"Matches while the {owner}'s source is below this number."),
        new("equals", SchemaKind.String, $"Matches while the {owner}'s source is this number or this text."),
        new("stale", SchemaKind.Boolean, $"Matches while the {owner}'s source has no fresh value. It only takes true."),
    ];

    // Properties, not fields: the blocks are built from static initializers in other files, which may run first.
    static string[] ConditionNames => ["above", "below", "equals", "stale"];

    static SchemaBlock WhenBlock() => new("when", "when", "When rules",
        "Each rule has one condition and a style to layer on while it holds. Every matching rule applies, in order. "
        + "Above, below and equals compare the widget's `source`; a text widget's stale also matches while any value "
        + "in its text is missing.",
        [
            .. Conditions("widget"),
            new("style", SchemaKind.Style, "The style to layer on: a style's name, or a style written in place.") { Required = true },
        ])
    {
        ExactlyOneOf = ConditionNames,
    };
}
