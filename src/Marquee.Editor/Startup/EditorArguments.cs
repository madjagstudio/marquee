using Marquee.Core.Config;
using Marquee.Editor.Resources;
using Marquee.Host;

namespace Marquee.Editor.Startup;

/// The theme folder to open, or why there is none.
public sealed record ThemeTarget(string? Folder, string? Error)
{
    public static ThemeTarget Found(string folder) => new(folder, null);

    public static ThemeTarget Failed(string error) => new(null, error);
}

/// Which theme the editor opens from its command line:
/// - `MarqueeEditor <theme or folder>` (as `marquee edit` starts it): found as `marquee run` finds a theme
///   (ThemeLocator), a path from the current folder and a name in the theme folders;
/// - no arguments (as the tray's "Edit theme…" starts it, through Explorer, which can't pass any): the theme
///   config.yaml names, the one on the screen, found from config.yaml's folder as the tray finds it;
/// - and when config.yaml names none, or can't be read, the tray's default example, as the tray then shows it.
public static class EditorArguments
{
    /// The theme the tray shows when config.yaml names none (FirstRun.DefaultTheme).
    public const string DefaultTheme = "example-landscape";

    public static ThemeTarget Resolve(IReadOnlyList<string> args, ThemeLocator locator, string configFile)
    {
        if (args.Count > 1) return ThemeTarget.Failed(Strings.Usage);

        if (args.Count == 1)
        {
            return locator.TryLocate(args[0], out var folder, out var error)
                ? ThemeTarget.Found(folder)
                : ThemeTarget.Failed(error);
        }

        var theme = ConfiguredTheme(configFile) ?? DefaultTheme;
        var fromConfig = locator.RelativeTo(Path.GetDirectoryName(Path.GetFullPath(configFile))!);
        return fromConfig.TryLocate(theme, out var configured, out var notFound)
            ? ThemeTarget.Found(configured)
            : ThemeTarget.Failed(notFound);
    }

    /// config.yaml's `theme:`; null when it names none, or isn't there, or can't be read or parsed: the tray uses its
    /// defaults then (ConfigFile.Load), and shows the default example.
    static string? ConfiguredTheme(string configFile)
    {
        try
        {
            return ConfigFile.Read(configFile).Theme;
        }
        catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
