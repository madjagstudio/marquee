using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// The video widget, and the limit on videos a theme plays, which its video layers and video widgets share.
static partial class WidgetParser
{
    /// Each video decodes on its own background thread and holds several frames in memory, wherever it is drawn, so a
    /// theme may play at most this many, counting its video layers (at most BackgroundParser.MaxVideoLayers) and its
    /// video widgets together. Hidden ones count: unhiding one must not break the limit.
    public const int MaxVideos = 4;

    /// Most pixels a video widget's box may cover: the canvas's, 1920×480 either way round. Its player scales each frame
    /// to the box and holds several at once, so a box this large costs what a video layer does, and a theme can't ask
    /// for gigabytes with a large one.
    public const int MaxVideoPixels = 1920 * 480;

    /// Takes the clips a video layer takes, and covers its box by default as a video layer covers the canvas.
    static VideoWidget Video(Fields f)
    {
        var sizeNode = f.Required("size");
        var size = Yaml.Pair(sizeNode, "size", min: 1);
        var pixels = (double)size.X * size.Y;
        if (pixels > MaxVideoPixels)
        {
            throw Yaml.Error(sizeNode, string.Create(CultureInfo.InvariantCulture,
                $"a video widget can cover at most {MaxVideoPixels:N0} pixels, as many as the canvas; this one covers {pixels:N0}"));
        }

        return new VideoWidget(BackgroundParser.VideoPath(f.Required("src"), "src"),
            f.Optional("fit") is { } fit ? BackgroundParser.Fit(fit, "fit") : ImageFit.Cover);
    }

    /// False for a video widget past the theme's limit, which is left out. Only the first is reported, as for the
    /// background's limits: the ones after it are over for the same reason.
    static bool WithinVideoLimit(WidgetSpec widget, YamlNode node, Context context)
    {
        if (widget is not VideoWidget || ++context.Videos <= MaxVideos) return true;
        if (context.Videos == MaxVideos + 1)
            context.Errors.Report(Yaml.Error(node, $"a theme can have at most {MaxVideos} videos, counting its video layers and video widgets"));
        return false;
    }
}
