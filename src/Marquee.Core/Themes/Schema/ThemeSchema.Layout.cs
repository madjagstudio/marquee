using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock LayoutBlock() => new("layout", "layout.yaml", "layout.yaml",
        "The theme's main file. It needs a `canvas`; everything else is optional.",
        [
            new("canvas", SchemaKind.Block, "The screen the theme is made for: which way round, and how many frames a second.")
            {
                Required = true, Of = "canvas",
            },
            new("styles", SchemaKind.Map, "Named styles, which widgets and `when` rules use by name.") { Of = "style" },
            new("background", SchemaKind.List,
                $"Layers drawn behind the widgets, first at the bottom: up to {BackgroundParser.MaxLayers}, at most "
                + $"{BackgroundParser.MaxShaderLayers} of them shaders and {BackgroundParser.MaxVideoLayers} videos.")
            {
                Of = "layer", MaxItems = BackgroundParser.MaxLayers,
            },
            new("widgets", SchemaKind.List,
                $"What's drawn on the screen, in order, so later widgets cover earlier ones. Up to {WidgetParser.MaxWidgets} "
                + "in all, counting the ones in groups.")
            {
                Of = "widget",
            },
            new("ring", SchemaKind.Block, "What the screen's LED ring shows. Left out, the ring is a soft warm white at 40%.")
            {
                Of = "ring",
            },
        ]);
}
