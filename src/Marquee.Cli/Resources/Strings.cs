using System.Globalization;
using System.Resources;

/// The CLI's own messages, from Strings.resx in English and from Strings.<culture>.resx where a translation exists.
/// Each property (plain text) or method (text with placeholders) reads the resource of the same name; the tests check
/// that the two lists match. Messages from the libraries (Marquee.Core, the providers) are still English-only.
internal static class Strings
{
    /// Settable so tests can put a pseudo-translation in front of the real resources.
    internal static ResourceManager ResourceManager { get; set; } = new("Marquee.Cli.Resources.Strings", typeof(Strings).Assembly);

    public static string Usage => Get(nameof(Usage));

    public static string LogWarning(string message) => Format(nameof(LogWarning), message);

    public static string LogError(string message) => Format(nameof(LogError), message);

    public static string NoSupportedScreens => Get(nameof(NoSupportedScreens));

    public static string NoScreenFound => Get(nameof(NoScreenFound));

    public static string NoScreenFoundUseSink => Get(nameof(NoScreenFoundUseSink));

    public static string CouldNotReadImage(string file) => Format(nameof(CouldNotReadImage), file);

    public static string ImageStretchedToPortrait(int imageWidth, int imageHeight, int canvasWidth, int canvasHeight) =>
        Format(nameof(ImageStretchedToPortrait), imageWidth, imageHeight, canvasWidth, canvasHeight);

    public static string ImageStretchedToLandscape(int imageWidth, int imageHeight, int canvasWidth, int canvasHeight) =>
        Format(nameof(ImageStretchedToLandscape), imageWidth, imageHeight, canvasWidth, canvasHeight);

    public static string SentImage(string file, string screen) => Format(nameof(SentImage), file, screen);

    public static string SentImageWithFirmware(string file, string screen, string firmware) =>
        Format(nameof(SentImageWithFirmware), file, screen, firmware);

    public static string PushedFrames(int frames, double averageMs) => Format(nameof(PushedFrames), frames, averageMs);

    public static string ClaudeSessionsSource => Get(nameof(ClaudeSessionsSource));

    public static string ClaudeLimitsSource => Get(nameof(ClaudeLimitsSource));

    public static string CodexLimitsSource => Get(nameof(CodexLimitsSource));

    public static string CodexSessionsSource => Get(nameof(CodexSessionsSource));

    public static string SourceUnavailable(string source, string reason) => Format(nameof(SourceUnavailable), source, reason);

    public static string SourceFailed(string source, string reason) => Format(nameof(SourceFailed), source, reason);

    public static string NoReadings => Get(nameof(NoReadings));

    public static string ShowingTheme(string theme) => Format(nameof(ShowingTheme), theme);

    public static string LastGoodLayoutStays(string error) => Format(nameof(LastGoodLayoutStays), error);

    public static string FramesNotDelivered(long delivered, int requested, string screen) =>
        Format(nameof(FramesNotDelivered), delivered, requested, screen);

    public static string RenderedFrames(int frames, string screen) => Format(nameof(RenderedFrames), frames, screen);

    public static string RenderingTo(string screen) => Format(nameof(RenderingTo), screen);

    public static string RenderStatus(double fps, int targetFps, double renderMs, double sendMs, long framesSent) =>
        Format(nameof(RenderStatus), fps, targetFps, renderMs, sendMs, framesSent);

    public static string InvalidCustomKey(string key, int maxLength) => Format(nameof(InvalidCustomKey), key, maxLength);

    public static string UnknownProgram => Get(nameof(UnknownProgram));

    public static string RunSetupFromMarquee(string program) => Format(nameof(RunSetupFromMarquee), program);

    public static string DryRunWouldWrite(string settingsPath) => Format(nameof(DryRunWouldWrite), settingsPath);

    public static string WithoutDryRun(string message) => Format(nameof(WithoutDryRun), message);

    public static string LimitsAppearAfterSetup => Get(nameof(LimitsAppearAfterSetup));

    public static string CouldNotUpdateClaudeSettings(string reason) => Format(nameof(CouldNotUpdateClaudeSettings), reason);

    // The user's UI language picks the text. A .resx is XML, which reads every line break as "\n"; multi-line
    // messages such as the usage get the platform's line breaks back, like the rest of the console output.
    static string Get(string name) =>
        (ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"There is no string resource named {name}."))
        .ReplaceLineEndings();

    // Numbers inside messages stay culture-invariant, like every other number Marquee prints.
    static string Format(string name, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(name), args);
}
