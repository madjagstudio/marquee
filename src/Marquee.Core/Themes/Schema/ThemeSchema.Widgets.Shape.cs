namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock ShapeWidget()
    {
        var arc = ("shape", "arc");
        return new("widget.shape", "shape", "Shape",
            "A rectangle, ellipse, line or arc, drawn with its style's `fill`, `stroke` and `opacity`, and a `shadow` and "
            + "`glow` that follow its outline as they follow text. A `source` and `when` rules can restyle it, so a shape can "
            + "change color on a condition.",
            [
                new("size", SchemaKind.Size, "The shape's width and height, each at least 1; a line's may be 0 one way, which makes it "
                    + "horizontal or vertical.")
                {
                    Required = true, Min = 1, MinFor = ("shape", "line", 0), Unit = SchemaUnit.Px,
                },
                new("shape", SchemaKind.Enum, "Which shape: a rect has the style's `radius` corners, an ellipse fills the box, a line "
                    + "runs from the top-left corner to the bottom-right and is drawn by its stroke alone (which it must have), "
                    + "and an arc is a band like a gauge's.")
                {
                    Required = true, Values = ["rect", "ellipse", "line", "arc"],
                },
                new("start", SchemaKind.Number, "Where the arc starts, clockwise from 3 o'clock.")
                {
                    Default = Number(Arcs.DefaultStart), Min = -Arcs.MaxAngle, Max = Arcs.MaxAngle, Unit = SchemaUnit.Degrees, OnlyFor = arc,
                },
                new("sweep", SchemaKind.Number, "How far the arc runs from its start; negative runs anticlockwise. It can't be 0.")
                {
                    Default = Number(Arcs.DefaultSweep), Min = -Arcs.MaxAngle, Max = Arcs.MaxAngle, NotZero = true, Unit = SchemaUnit.Degrees,
                    OnlyFor = arc,
                },
                new("thickness", SchemaKind.Number, "How thick the arc's band is. The style's `stroke.cap` shapes its ends.")
                {
                    Default = Number(Arcs.DefaultThickness), Min = 1, Unit = SchemaUnit.Px, OnlyFor = arc,
                },
            ]);
    }
}
