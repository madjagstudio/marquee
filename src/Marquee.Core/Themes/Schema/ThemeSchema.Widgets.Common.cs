namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    /// A new widget type is a file of its own and one entry in this list.
    static SchemaUnion WidgetUnion() => new("widget", "Widgets",
        "Each entry of `widgets:`, and of a group's `children:`, is one widget, chosen by its `type`.",
        WidgetCommon(),
        [TextWidget(), ValueWidget(), BarWidget(), GaugeWidget(), GraphWidget(), ImageWidget(), GroupWidget()])
    {
        Discriminator = "type",
        DiscriminatorHelp = "What kind of widget this is.",
    };

    static SchemaBlock WidgetCommon() => new("widget.common", "widget", "Every widget", "Every widget takes these.",
        [
            new("at", SchemaKind.Point, "Where the widget's top-left corner goes, relative to the canvas or its group.")
            {
                Required = true, Unit = SchemaUnit.Px,
            },
            new("size", SchemaKind.Size, "The widget's width and height, each at least 1.") { Required = true, Unit = SchemaUnit.Px },
            Source("The value the widget shows, and what its `when` rules compare: a sensor key such as cpu.temp, or an "
                + "expression. Value, bar, gauge and graph widgets need one."),
            new("style", SchemaKind.Style, "How the widget looks: a style's name from `styles:`, or a style written in place."),
            new("when", SchemaKind.List,
                "Styles to layer on while a condition holds, in order: every rule that matches applies, so a later one wins.")
            {
                Of = "when",
            },
            new("smooth", SchemaKind.Number, "Eases a value, bar or gauge to a new number over this long; 0 jumps straight there.")
            {
                Default = "0", Min = 0, Max = 10_000, Unit = SchemaUnit.Ms,
            },
        ]);

    /// The common `source`, or a widget type's own, which may make it required.
    static SchemaKey Source(string help, bool required = false) =>
        new("source", SchemaKind.Expression, help) { Required = required };

    /// A bar's or gauge's `min` and `max`.
    static SchemaKey[] Range(string empty, string full) =>
    [
        new("min", SchemaKind.Number, $"The value at which the {empty}.") { Default = "0" },
        new("max", SchemaKind.Number, $"The value at which the {full}. It must be more than min.") { Default = "100" },
    ];
}
