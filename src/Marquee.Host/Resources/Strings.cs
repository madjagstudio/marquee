using System.Globalization;
using System.Resources;

namespace Marquee.Host.Resources;

/// The engine's own messages, which reach the user through the log (the console of `marquee run`, the tray's log
/// file), from Strings.resx in English and from Strings.<culture>.resx where a translation exists. Each property (plain
/// text) or method (text with placeholders) reads the resource of the same name; the tests check that the two lists
/// match.
internal static class Strings
{
    /// Settable so tests can put a pseudo-translation in front of the real resources.
    internal static ResourceManager ResourceManager { get; set; } = new("Marquee.Host.Resources.Strings", typeof(Strings).Assembly);

    public static string ClaudeSessionsSource => Get(nameof(ClaudeSessionsSource));

    public static string ClaudeLimitsSource => Get(nameof(ClaudeLimitsSource));

    public static string CodexLimitsSource => Get(nameof(CodexLimitsSource));

    public static string CodexSessionsSource => Get(nameof(CodexSessionsSource));

    public static string SourceUnavailable(string source, string reason) => Format(nameof(SourceUnavailable), source, reason);

    public static string FpsNeedsAdministrator => Get(nameof(FpsNeedsAdministrator));

    public static string NoTheme => Get(nameof(NoTheme));

    public static string ThemeFolderNotFound(string folder) => Format(nameof(ThemeFolderNotFound), folder);

    public static string ThemeNotFound(string theme, string places) => Format(nameof(ThemeNotFound), theme, places);

    public static string ConfigUnreadable(string path, string reason) => Format(nameof(ConfigUnreadable), path, reason);

    public static string ConfigInvalidUsingDefaults(string error) => Format(nameof(ConfigInvalidUsingDefaults), error);

    public static string CouldNotReadConfig(string reason) => Format(nameof(CouldNotReadConfig), reason);

    public static string CouldNotApplyConfig(string reason) => Format(nameof(CouldNotApplyConfig), reason);

    public static string NoScreenFound => Get(nameof(NoScreenFound));

    public static string ShowingTheme(string theme) => Format(nameof(ShowingTheme), theme);

    public static string LastGoodLayoutStays(string error) => Format(nameof(LastGoodLayoutStays), error);

    public static string SettingsChanged => Get(nameof(SettingsChanged));

    public static string PreviousSettingsKept => Get(nameof(PreviousSettingsKept));

    public static string SavingLimitsFailed(string reason) => Format(nameof(SavingLimitsFailed), reason);

    public static string StartFailed(string reason) => Format(nameof(StartFailed), reason);

    public static string SettingsChangeFailed(string reason) => Format(nameof(SettingsChangeFailed), reason);

    public static string HandlerFailed(string reason) => Format(nameof(HandlerFailed), reason);

    // The user's UI language picks the text. A .resx is XML, which reads every line break as "\n"; multi-line
    // messages get the platform's line breaks back, like the rest of the log.
    static string Get(string name) =>
        (ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"There is no string resource named {name}."))
        .ReplaceLineEndings();

    // Numbers inside messages stay culture-invariant, like every other number Marquee prints.
    static string Format(string name, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(name), args);
}
