using System.Globalization;
using System.Resources;

/// The CLI's own messages, from Strings.resx in English and from Strings.<culture>.resx where a translation exists.
/// Each property (plain text) or method (text with placeholders) reads the resource of the same name; the tests check
/// that the two lists match. The engine's messages (Marquee.Host) are in its own Strings.resx; those from Marquee.Core
/// and the providers are still English-only.
internal static class Strings
{
    /// Settable so tests can put a pseudo-translation in front of the real resources.
    internal static ResourceManager ResourceManager { get; set; } = new("Marquee.Cli.Resources.Strings", typeof(Strings).Assembly);

    public static string Usage => Get(nameof(Usage));

    public static string LogWarning(string message) => Format(nameof(LogWarning), message);

    public static string LogError(string message) => Format(nameof(LogError), message);

    public static string NoSupportedScreens => Get(nameof(NoSupportedScreens));

    public static string NoScreenFound => Get(nameof(NoScreenFound));

    public static string CouldNotReadImage(string file) => Format(nameof(CouldNotReadImage), file);

    public static string ImageStretchedToPortrait(int imageWidth, int imageHeight, int canvasWidth, int canvasHeight) =>
        Format(nameof(ImageStretchedToPortrait), imageWidth, imageHeight, canvasWidth, canvasHeight);

    public static string ImageStretchedToLandscape(int imageWidth, int imageHeight, int canvasWidth, int canvasHeight) =>
        Format(nameof(ImageStretchedToLandscape), imageWidth, imageHeight, canvasWidth, canvasHeight);

    public static string SentImage(string file, string screen) => Format(nameof(SentImage), file, screen);

    public static string SentImageWithFirmware(string file, string screen, string firmware) =>
        Format(nameof(SentImageWithFirmware), file, screen, firmware);

    public static string PushedFrames(int frames, double averageMs) => Format(nameof(PushedFrames), frames, averageMs);

    public static string SourceFailed(string source, string reason) => Format(nameof(SourceFailed), source, reason);

    public static string NoReadings => Get(nameof(NoReadings));

    public static string ConfigFileMissing(string path) => Format(nameof(ConfigFileMissing), path);

    public static string DataFolderUnusable(string reason) => Format(nameof(DataFolderUnusable), reason);

    public static string SettingsUnchanged(string error) => Format(nameof(SettingsUnchanged), error);

    public static string FramesNotDelivered(long delivered, int requested, string screen) =>
        Format(nameof(FramesNotDelivered), delivered, requested, screen);

    public static string RenderedFrames(int frames, string screen) => Format(nameof(RenderedFrames), frames, screen);

    public static string RenderingTo(string screen) => Format(nameof(RenderingTo), screen);

    public static string RenderStatus(double fps, int targetFps, double renderMs, double sendMs, long framesSent) =>
        Format(nameof(RenderStatus), fps, targetFps, renderMs, sendMs, framesSent);

    public static string InvalidCustomKey(string key, int maxLength) => Format(nameof(InvalidCustomKey), key, maxLength);

    public static string HttpOffInConfig(string configPath) => Format(nameof(HttpOffInConfig), configPath);

    public static string UnknownProgram => Get(nameof(UnknownProgram));

    public static string RunSetupFromMarquee(string program) => Format(nameof(RunSetupFromMarquee), program);

    public static string DryRunWouldWrite(string settingsPath) => Format(nameof(DryRunWouldWrite), settingsPath);

    public static string WithoutDryRun(string message) => Format(nameof(WithoutDryRun), message);

    public static string LimitsAppearAfterSetup => Get(nameof(LimitsAppearAfterSetup));

    public static string CouldNotUpdateClaudeSettings(string reason) => Format(nameof(CouldNotUpdateClaudeSettings), reason);

    public static string ValidateErrorOnLine(string file, int line, string message) => Format(nameof(ValidateErrorOnLine), file, line, message);

    public static string ValidateError(string file, string message) => Format(nameof(ValidateError), file, message);

    public static string ValidateWarningOnLine(string file, int line, string message) =>
        Format(nameof(ValidateWarningOnLine), file, line, message);

    public static string ValidateWarning(string file, string message) => Format(nameof(ValidateWarning), file, message);

    public static string ValidateSummary(string theme, string orientation, int width, int height, string counts) =>
        Format(nameof(ValidateSummary), theme, orientation, width, height, counts);

    public static string ValidateSummaryNoCanvas(string theme, string counts) => Format(nameof(ValidateSummaryNoCanvas), theme, counts);

    public static string ValidateZipElevated => Get(nameof(ValidateZipElevated));

    public static string NoProblemsFound => Get(nameof(NoProblemsFound));

    public static string OneError => Get(nameof(OneError));

    public static string ManyErrors(int count) => Format(nameof(ManyErrors), count);

    public static string OneWarning => Get(nameof(OneWarning));

    public static string ManyWarnings(int count) => Format(nameof(ManyWarnings), count);

    public static string ErrorsAndWarnings(string errors, string warnings) => Format(nameof(ErrorsAndWarnings), errors, warnings);

    // The user's UI language picks the text. A .resx is XML, which reads every line break as "\n"; multi-line
    // messages such as the usage get the platform's line breaks back, like the rest of the console output.
    static string Get(string name) =>
        (ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"There is no string resource named {name}."))
        .ReplaceLineEndings();

    // Numbers inside messages stay culture-invariant, like every other number Marquee prints.
    static string Format(string name, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(name), args);
}
