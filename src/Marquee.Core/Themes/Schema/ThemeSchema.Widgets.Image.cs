namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    static SchemaBlock ImageWidget() => new("widget.image", "image", "Image",
        "A picture from the theme folder, over the style's `fill` and under its stroke, clipped to the box.",
        [
            new("src", SchemaKind.ThemeFile, "The image file, relative to the theme folder: PNG, JPEG, WebP or another format Skia reads.")
            {
                Required = true,
            },
            Fit("contain"),
        ]);
}
