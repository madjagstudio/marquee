using System.Globalization;

namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock CanvasBlock() => new("canvas", "canvas", "Canvas",
        "The screen the theme is made for. A theme fits both ways up of its orientation.",
        [
            new("orientation", SchemaKind.Enum, "How the screen is mounted: landscape is 1920×480, portrait 480×1920.")
            {
                Required = true, Values = ["portrait", "landscape"],
            },
            new("fps", SchemaKind.Integer, "Frames drawn each second.")
            {
                Default = CanvasSpec.DefaultFps.ToString(CultureInfo.InvariantCulture), Min = 1, Max = CanvasSpec.MaxFps,
            },
        ]);
}
