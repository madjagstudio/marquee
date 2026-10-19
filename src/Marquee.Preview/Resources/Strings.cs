using System.Globalization;
using System.Resources;

namespace Marquee.Preview.Resources;

/// The preview window's text, from Strings.resx in English and from Strings.<culture>.resx where a translation
/// exists. Each property (plain text) or method (text with placeholders) reads the resource of the same name; the
/// tests check that the two lists match.
internal static class Strings
{
    /// Settable so tests can put a pseudo-translation in front of the real resources.
    internal static ResourceManager ResourceManager { get; set; } = new("Marquee.Preview.Resources.Strings", typeof(Strings).Assembly);

    public static string WindowTitle(string theme) => Format(nameof(WindowTitle), theme);

    public static string WaitingForFirstFrame => Get(nameof(WaitingForFirstFrame));

    public static string CouldNotReadFramePixels => Get(nameof(CouldNotReadFramePixels));

    // The user's UI language picks the text. A .resx is XML, which reads every line break as "\n"; multi-line
    // messages get the platform's line breaks back.
    static string Get(string name) =>
        (ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"There is no string resource named {name}."))
        .ReplaceLineEndings();

    // Numbers inside messages stay culture-invariant, like every other number Marquee shows.
    static string Format(string name, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(name), args);
}
