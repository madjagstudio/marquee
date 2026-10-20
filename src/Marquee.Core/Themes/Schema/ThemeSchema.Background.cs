using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Themes.Schema;

public static partial class ThemeSchema
{
    /// How many values one shader uniform takes at most (a float4x4's 16).
    public const int MaxUniformValues = BackgroundParser.MaxComponents;

    /// What a uniform's name must look like.
    public const string UniformNamePattern = BackgroundParser.UniformNamePattern;

    /// The uniforms Marquee sets itself, which a theme can't bind.
    public static IReadOnlyList<string> ReservedUniforms => BackgroundParser.BuiltInUniforms;

    /// A background layer's and a style's `blend`, in the parser's order. A property, so it doesn't depend on the order
    /// the partial files' static fields are initialised in.
    static string[] BlendModes => ["normal", "multiply", "screen", "overlay", "darken", "lighten", "add"];

    static SchemaUnion BackgroundUnion() => new("layer", "Background layers",
        "Each entry of `background:` is one layer, chosen by which of `fill`, `image`, `shader` or `video` it has. Layers "
        + "cover the whole canvas and are drawn in order, the first at the bottom.",
        new SchemaBlock("layer.common", "layer", "Every layer", "Every layer takes these.",
        [
            new("opacity", SchemaKind.Number, "How solid the layer is: 0 hides it, 1 is fully solid.")
            {
                Default = "1", Min = 0, Max = 1, Unit = SchemaUnit.Fraction,
            },
            new("blend", SchemaKind.Enum, "How the layer mixes with the layers under it.")
            {
                Default = "normal", Values = BlendModes,
            },
            Hidden("Leaves the layer off the screen; `marquee validate` still checks it."),
            Locked("Stops the theme editor changing the layer. The screen ignores it."),
        ]),
        [
            new("layer.fill", "fill", "Fill layer", "A color or gradient.",
            [
                new("fill", SchemaKind.Paint, "The color or gradient.") { Required = true },
            ]),
            new("layer.image", "image", "Image layer", "A picture from the theme folder.",
            [
                new("image", SchemaKind.ThemeFile, "The image file, relative to the theme folder: PNG, JPEG, WebP or another format Skia reads.")
                {
                    Required = true,
                },
                Fit("cover"),
            ]),
            new("layer.shader", "shader", "Shader layer",
                "An SkSL shader that paints the canvas each frame. Marquee sets its `time`, `day` and `resolution` uniforms.",
            [
                new("shader", SchemaKind.ThemeFile, "The shader file (.sksl), relative to the theme folder.") { Required = true },
                new("uniforms", SchemaKind.Uniforms,
                    $"Binds the shader's other uniforms to numbers or expressions, one per component (up to {MaxUniformValues}). "
                    + "A missing or stale value is 0.")
                {
                    MaxItems = BackgroundParser.MaxUniforms,
                },
                new("scale", SchemaKind.Number,
                    "Renders the shader at this fraction of the canvas size and stretches it up, which costs far less.")
                {
                    Default = "1", Min = 0.1, Max = 1, Unit = SchemaUnit.Fraction,
                },
            ]),
            new("layer.video", "video", "Video layer", "A clip from the theme folder, played over and over.",
            [
                new("video", SchemaKind.ThemeFile, "The clip, relative to the theme folder.")
                {
                    Required = true, FileTypes = BackgroundParser.VideoExtensions,
                },
                Fit("cover"),
            ]),
        ]);

    /// An image layer's, a video layer's and an image widget's `fit`, which differ only in their default.
    static SchemaKey Fit(string defaultFit) => new("fit", SchemaKind.Enum,
        "How the picture fills its box: cover crops to fill it, contain fits inside it, stretch distorts to fill it, and "
        + "none keeps its own size, centred.")
    {
        Default = defaultFit, Values = ["cover", "contain", "stretch", "none"],
    };
}
