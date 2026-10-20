using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    /// Most videos a theme plays, its video layers and video widgets together.
    public const int MaxVideos = WidgetParser.MaxVideos;

    /// Most pixels a video widget's box may cover, its width times its height: the canvas's.
    public const int MaxVideoPixels = WidgetParser.MaxVideoPixels;

    static SchemaBlock VideoWidget() => new("widget.video", "video", "Video",
        "A clip from the theme folder, played over and over, over the style's `fill` and under its stroke, clipped to the "
        + "box. The box can cover at most as many pixels as the canvas (1920×480), and a theme can have at most "
        + $"{MaxVideos} videos, counting its video layers and video widgets.",
        [
            new("src", SchemaKind.ThemeFile, "The clip, relative to the theme folder.")
            {
                Required = true, FileTypes = BackgroundParser.VideoExtensions,
            },
            Fit("cover"),
        ]);
}
