namespace Marquee.Core;

/// Where Marquee keeps its own files. All of it is per user, under %LOCALAPPDATA%, and the uninstaller leaves it.
public static class MarqueePaths
{
    /// %LOCALAPPDATA%\Marquee: Marquee's own state (config, logs, the user's themes, saved Claude data).
    public static string DataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Marquee");

    /// The daily log files FileLog writes, which the tray's "Open logs" opens.
    public static string Logs => Path.Combine(DataRoot, "logs");

    /// The user's own themes, looked up by name before the installed examples.
    public static string Themes => Path.Combine(DataRoot, "themes");

    /// config.yaml: the theme, orientation, providers and other settings `marquee run` and the tray start with.
    public static string ConfigFile => Path.Combine(DataRoot, "config.yaml");
}
