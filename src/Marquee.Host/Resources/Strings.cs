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

    public static string WaitingForTheme(string theme) => Format(nameof(WaitingForTheme), theme);

    public static string ThemeStillFails(string error) => Format(nameof(ThemeStillFails), error);

    public static string SavingLimitsFailed(string reason) => Format(nameof(SavingLimitsFailed), reason);

    public static string StartFailed(string reason) => Format(nameof(StartFailed), reason);

    public static string SettingsChangeFailed(string reason) => Format(nameof(SettingsChangeFailed), reason);

    public static string HandlerFailed(string reason) => Format(nameof(HandlerFailed), reason);

    public static string LogonTaskDescription => Get(nameof(LogonTaskDescription));

    public static string SchTasksFailed(int exitCode, string output) => Format(nameof(SchTasksFailed), exitCode, output);

    public static string TaskFileChanged(string file) => Format(nameof(TaskFileChanged), file);

    public static string SchTasksTimedOut(int seconds) => Format(nameof(SchTasksTimedOut), seconds);

    public static string SchTasksDidNotStart => Get(nameof(SchTasksDidNotStart));

    public static string TaskSchedulerFailed(string reason) => Format(nameof(TaskSchedulerFailed), reason);

    public static string UpdatesFolderIsLink(string folder) => Format(nameof(UpdatesFolderIsLink), folder);

    public static string RingUnavailable(string reason, int seconds) => Format(nameof(RingUnavailable), reason, seconds);

    public static string RingNotFound => Get(nameof(RingNotFound));

    public static string RingConnected => Get(nameof(RingConnected));

    public static string RingFrameFailed(string reason) => Format(nameof(RingFrameFailed), reason);

    public static string HandoverAuto => Get(nameof(HandoverAuto));

    public static string HandoverRetry => Get(nameof(HandoverRetry));

    public static string HandoverScreenOpen => Get(nameof(HandoverScreenOpen));

    public static string HandoverHeldBack(int seconds) => Format(nameof(HandoverHeldBack), seconds);

    public static string HandoverNeedsAdministrator => Get(nameof(HandoverNeedsAdministrator));

    public static string TakeOverNeedsAdministrator => Get(nameof(TakeOverNeedsAdministrator));

    public static string HandoverFailed(string reason) => Format(nameof(HandoverFailed), reason);

    public static string ReleaseNotJson(string reason) => Format(nameof(ReleaseNotJson), reason);

    public static string ReleaseUnreadable => Get(nameof(ReleaseUnreadable));

    public static string ReleaseIsDraft(string tag) => Format(nameof(ReleaseIsDraft), tag);

    public static string ReleaseIsPrerelease(string tag) => Format(nameof(ReleaseIsPrerelease), tag);

    public static string ReleaseTagNotAVersion(string tag) => Format(nameof(ReleaseTagNotAVersion), tag);

    public static string ReleaseFileMissing(string tag, string file) => Format(nameof(ReleaseFileMissing), tag, file);

    public static string ReleaseFileUnreadable(string file) => Format(nameof(ReleaseFileUnreadable), file);

    public static string ReleaseFileSize(string file, long size, long maxSize) => Format(nameof(ReleaseFileSize), file, size, maxSize);

    public static string ReleaseFileNotOnGitHub(string file) => Format(nameof(ReleaseFileNotOnGitHub), file);

    public static string UpdateCheckSkippedDevelopmentKey => Get(nameof(UpdateCheckSkippedDevelopmentKey));

    public static string UpdateCheckSkippedNoVersion(string version) => Format(nameof(UpdateCheckSkippedNoVersion), version);

    public static string UpdateCheckTimedOut(double seconds) => Format(nameof(UpdateCheckTimedOut), seconds);

    public static string UpdateCheckFailed(string reason) => Format(nameof(UpdateCheckFailed), reason);

    public static string UpdateCheckHttpError(int status, string reason) => Format(nameof(UpdateCheckHttpError), status, reason);

    public static string UpdateCheckRateLimited => Get(nameof(UpdateCheckRateLimited));

    public static string NoReleaseYet => Get(nameof(NoReleaseYet));

    public static string UpdateCheckAnswerTooLarge(int maxBytes) => Format(nameof(UpdateCheckAnswerTooLarge), maxBytes);

    public static string UpdateCheckUnusableRelease(string problem) => Format(nameof(UpdateCheckUnusableRelease), problem);

    public static string UpToDate(string version) => Format(nameof(UpToDate), version);

    public static string UpdateAvailable(string version, string running) => Format(nameof(UpdateAvailable), version, running);

    public static string UpdateCheckTimeNotSaved(string path, string reason) => Format(nameof(UpdateCheckTimeNotSaved), path, reason);

    public static string UpdateRefusedDevelopmentKey => Get(nameof(UpdateRefusedDevelopmentKey));

    public static string UpdateSignatureMissing(string file) => Format(nameof(UpdateSignatureMissing), file);

    public static string UpdateSignatureTooLarge(string file, int maxBytes) => Format(nameof(UpdateSignatureTooLarge), file, maxBytes);

    public static string UpdateSignatureInvalid(string file) => Format(nameof(UpdateSignatureInvalid), file);

    public static string UpdateWrongSize(string file, long size) => Format(nameof(UpdateWrongSize), file, size);

    public static string UpdateDownloadHttpError(string file, int status) => Format(nameof(UpdateDownloadHttpError), file, status);

    public static string UpdateDownloadFailed(string reason) => Format(nameof(UpdateDownloadFailed), reason);

    public static string UpdateDownloadStalled(double seconds) => Format(nameof(UpdateDownloadStalled), seconds);

    public static string UpdateVerified(string file) => Format(nameof(UpdateVerified), file);

    public static string InstallerStarting(string version) => Format(nameof(InstallerStarting), version);

    public static string InstallerDidNotStart(string reason) => Format(nameof(InstallerDidNotStart), reason);

    // The user's UI language picks the text. A .resx is XML, which reads every line break as "\n"; multi-line
    // messages get the platform's line breaks back, like the rest of the log.
    static string Get(string name) =>
        (ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"There is no string resource named {name}."))
        .ReplaceLineEndings();

    // Numbers inside messages stay culture-invariant, like every other number Marquee prints.
    static string Format(string name, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(name), args);
}
