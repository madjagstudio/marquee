using System.Globalization;
using System.Resources;

namespace Marquee.App.Resources;

/// The tray's text (its menu, balloons and log lines), from Strings.resx in English and from Strings.<culture>.resx
/// where a translation exists. Each property (plain text) or method (text with placeholders) reads the resource of the
/// same name; the tests check that the two lists match.
internal static class Strings
{
    /// Settable so tests can put a pseudo-translation in front of the real resources.
    internal static ResourceManager ResourceManager { get; set; } = new("Marquee.App.Resources.Strings", typeof(Strings).Assembly);

    public static string AppName => Get(nameof(AppName));

    public static string Tooltip(string status) => Format(nameof(Tooltip), status);

    public static string StatusShowing(string theme, double fps, string screen) => Format(nameof(StatusShowing), theme, fps, screen);

    public static string StatusPaused => Get(nameof(StatusPaused));

    public static string StatusWaitingForScreen => Get(nameof(StatusWaitingForScreen));

    public static string StatusThemeFailed => Get(nameof(StatusThemeFailed));

    public static string StatusNoTheme => Get(nameof(StatusNoTheme));

    public static string StatusLConnectBusy => Get(nameof(StatusLConnectBusy));

    public static string StatusDownloading(string version, int percent) => Format(nameof(StatusDownloading), version, percent);

    public static string StatusInstalling(string version) => Format(nameof(StatusInstalling), version);

    public static string MenuThemes => Get(nameof(MenuThemes));

    public static string MenuNoThemes => Get(nameof(MenuNoThemes));

    public static string MenuOrientation => Get(nameof(MenuOrientation));

    public static string OrientationLandscape => Get(nameof(OrientationLandscape));

    public static string OrientationLandscapeFlipped => Get(nameof(OrientationLandscapeFlipped));

    public static string OrientationPortrait => Get(nameof(OrientationPortrait));

    public static string OrientationPortraitFlipped => Get(nameof(OrientationPortraitFlipped));

    public static string MenuOpenPreview => Get(nameof(MenuOpenPreview));

    public static string MenuOpenThemesFolder => Get(nameof(MenuOpenThemesFolder));

    public static string MenuOpenLogs => Get(nameof(MenuOpenLogs));

    public static string MenuEditTheme => Get(nameof(MenuEditTheme));

    public static string MenuEditSettings => Get(nameof(MenuEditSettings));

    public static string MenuPause => Get(nameof(MenuPause));

    public static string MenuResume => Get(nameof(MenuResume));

    public static string MenuTakeOver => Get(nameof(MenuTakeOver));

    public static string MenuAlwaysTakeOver => Get(nameof(MenuAlwaysTakeOver));

    public static string TakeOverNeedsAdministrator => Get(nameof(TakeOverNeedsAdministrator));

    public static string TakeOverTitle => Get(nameof(TakeOverTitle));

    public static string TakeOverPrompt => Get(nameof(TakeOverPrompt));

    public static string FirstRunTakeOverPrompt(string prompt, string menuItem) => Format(nameof(FirstRunTakeOverPrompt), prompt, menuItem);

    public static string HandoverFailedTitle => Get(nameof(HandoverFailedTitle));

    public static string HandoverFailed(string message) => Format(nameof(HandoverFailed), message);

    public static string HandoverFailedRetrying(string message) => Format(nameof(HandoverFailedRetrying), message);

    public static string HandoverFailedProblem(string message) => Format(nameof(HandoverFailedProblem), message);

    public static string MenuCheckForUpdates => Get(nameof(MenuCheckForUpdates));

    public static string MenuInstallUpdate(string version) => Format(nameof(MenuInstallUpdate), version);

    public static string MenuDownloadUpdate(string version) => Format(nameof(MenuDownloadUpdate), version);

    public static string UpdateAvailableTitle => Get(nameof(UpdateAvailableTitle));

    public static string UpdateAvailable(string version) => Format(nameof(UpdateAvailable), version);

    public static string UpdateAvailableDownload(string version) => Format(nameof(UpdateAvailableDownload), version);

    public static string UpToDateTitle => Get(nameof(UpToDateTitle));

    public static string UpToDate(string version) => Format(nameof(UpToDate), version);

    public static string UpdateCheckFailedTitle => Get(nameof(UpdateCheckFailedTitle));

    public static string UpdateFailedTitle => Get(nameof(UpdateFailedTitle));

    public static string UpdateFailed(string reason) => Format(nameof(UpdateFailed), reason);

    public static string UpdateDidNotInstall(int exitCode, string log) => Format(nameof(UpdateDidNotInstall), exitCode, log);

    public static string InstallerFinishedWithoutQuit(int exitCode) => Format(nameof(InstallerFinishedWithoutQuit), exitCode);

    public static string UpdateTimerFailed(string reason) => Format(nameof(UpdateTimerFailed), reason);

    public static string NoReleaseKeyTitle => Get(nameof(NoReleaseKeyTitle));

    public static string NoReleaseKey(string releasesPage) => Format(nameof(NoReleaseKey), releasesPage);

    public static string MenuAbout => Get(nameof(MenuAbout));

    public static string MenuQuit => Get(nameof(MenuQuit));

    public static string About(string version) => Format(nameof(About), version);

    public static string FirstRunHintTitle => Get(nameof(FirstRunHintTitle));

    public static string FirstRunHint => Get(nameof(FirstRunHint));

    public static string ThemeFailedTitle => Get(nameof(ThemeFailedTitle));

    public static string ThemeProblem(string error) => Format(nameof(ThemeProblem), error);

    public static string ConfigFailedTitle => Get(nameof(ConfigFailedTitle));

    public static string ConfigInvalid(string error) => Format(nameof(ConfigInvalid), error);

    public static string ConfigUnreadable(string reason) => Format(nameof(ConfigUnreadable), reason);

    public static string ConfigChangeInvalid(string error) => Format(nameof(ConfigChangeInvalid), error);

    public static string FpsNeedsAdministrator => Get(nameof(FpsNeedsAdministrator));

    public static string ConfigNotWatched(string reason) => Format(nameof(ConfigNotWatched), reason);

    public static string ScreenRetryFailed(string reason) => Format(nameof(ScreenRetryFailed), reason);

    public static string ScreenMarkerFailed(string reason) => Format(nameof(ScreenMarkerFailed), reason);

    public static string CommandFailedTitle => Get(nameof(CommandFailedTitle));

    public static string CommandFailed(string error) => Format(nameof(CommandFailed), error);

    public static string NoDesktopShell => Get(nameof(NoDesktopShell));

    public static string EditorMissing(string path) => Format(nameof(EditorMissing), path);

    public static string PreviewStatus(double fps, int targetFps, double renderMs, double sendMs, long framesSent) =>
        Format(nameof(PreviewStatus), fps, targetFps, renderMs, sendMs, framesSent);

    public static string DataFolderUnusable(string reason) => Format(nameof(DataFolderUnusable), reason);

    public static string TrayStarted(string version) => Format(nameof(TrayStarted), version);

    public static string TrayStopped => Get(nameof(TrayStopped));

    public static string QuitEventTaken(string name) => Format(nameof(QuitEventTaken), name);

    // The user's UI language picks the text. A .resx is XML, which reads every line break as "\n"; multi-line
    // messages get the platform's line breaks back.
    static string Get(string name) =>
        (ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"There is no string resource named {name}."))
        .ReplaceLineEndings();

    // Numbers inside messages stay culture-invariant, like every other number Marquee shows.
    static string Format(string name, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(name), args);
}
