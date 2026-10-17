using SkiaSharp;

namespace Marquee.Core.Themes;

/// Inter (SIL Open Font License 1.1), embedded in Marquee.Core. It is the default font and the fallback for any
/// font a theme names but the PC doesn't have, so a theme renders the same everywhere, snapshot tests included.
public static class BundledFonts
{
    public const string Family = "Inter";

    static readonly Lazy<SKTypeface> LazyRegular = new(() => Load("Inter-Regular.ttf"));
    static readonly Lazy<SKTypeface> LazyBold = new(() => Load("Inter-Bold.ttf"));

    public static SKTypeface Regular => LazyRegular.Value;
    public static SKTypeface Bold => LazyBold.Value;

    public static SKTypeface ForWeight(int weight) => weight >= 600 ? Bold : Regular;

    internal static byte[] ReadBytes(string file)
    {
        using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream($"Marquee.Fonts.{file}")
            ?? throw new InvalidOperationException($"Embedded font {file} is missing from Marquee.Core.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    static SKTypeface Load(string file)
    {
        using var data = SKData.CreateCopy(ReadBytes(file));
        return SKTypeface.FromData(data) ?? throw new InvalidOperationException($"Embedded font {file} could not be read.");
    }
}
