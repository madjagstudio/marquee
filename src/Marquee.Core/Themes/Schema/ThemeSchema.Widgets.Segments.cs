namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    /// A bar's or gauge's `segments` and `gap`: `widget` names it, `unit` and `gap` are the gap's unit and default, and
    /// `room` says how much each segment keeps.
    static SchemaKey[] Segments(string widget, SchemaUnit unit, float gap, string room) =>
    [
        new("segments", SchemaKind.Integer,
            $"Cuts the {widget} into this many equal segments, like a VU meter. A segment lights once the value reaches its "
            + "middle; `fill` paints the lit ones and `track` the rest, and `radius` rounds their corners.")
        {
            Min = SegmentSpec.MinCount, Max = SegmentSpec.MaxCount,
        },
        new("gap", SchemaKind.Number, $"The space between segments, used only with `segments`. {room}")
        {
            Default = Number(gap), Min = 0, Unit = unit,
        },
    ];
}
