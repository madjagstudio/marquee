using Marquee.Core.Config;
using Marquee.Host.Resources;

namespace Marquee.Host;

/// Settles what the engine runs with: a command-line flag, then config.yaml, then the default. The theme's own
/// canvas.fps comes after all three for the frame rate (FpsOverride stays null).
///
/// Orientation is the exception to "then the theme": with no flag and no config.yaml setting it is landscape, never the
/// theme's canvas orientation. Orientation is how the screen is mounted, which a theme can't know: a portrait theme
/// on a landscape mount is shown upright and letterboxed (with a warning saying how to fix it), where following the
/// theme would show it sideways. It would also tie the screen to the theme: the device is made for one orientation, so
/// every switch between a portrait and a landscape theme would close and reopen the screen, which a theme switch never
/// does otherwise.
public static class SettingsResolver
{
    /// `configFile` is where `config` was read from. A relative `theme:` in it, or a name found in none of the theme
    /// folders, is looked for from that file's folder rather than the current one: the tray, which Task Scheduler
    /// starts in System32, and `marquee run` in a terminal then read the same config.yaml the same way. A theme on the
    /// command line stays relative to the current folder, as anything typed in a terminal is. `configFile` is null when
    /// no config.yaml was read (a `--sink` run with no `--config`), and `config` is then MarqueeConfig.Default.
    public static SettingsResult Resolve(RunOptions options, MarqueeConfig config, ThemeLocator locator, string? configFile)
    {
        if (options.Theme is null && config.Theme is not null && configFile is not null)
            locator = locator.RelativeTo(Path.GetDirectoryName(Path.GetFullPath(configFile))!);
        if ((options.Theme ?? config.Theme) is not { } theme)
            return SettingsResult.Failure(Strings.NoTheme);
        if (!locator.TryLocate(theme, out var folder, out var error))
            return SettingsResult.Failure(error);

        var defaults = new EngineSettings { ThemeFolder = folder };
        var http = options.HttpEnabled ?? config.HttpEnabled;
        return SettingsResult.Success(defaults with
        {
            Orientation = options.Orientation ?? config.Orientation ?? defaults.Orientation,
            FpsOverride = options.Fps ?? config.Fps,
            Sensors = options.Sensors ?? config.Sensors ?? defaults.Sensors,
            HttpPort = http ? options.HttpPort ?? config.HttpPort ?? defaults.HttpPort : null,
            ClaudeSessions = config.ClaudeSessions,
            ClaudeUsage = options.ClaudeUsage ?? config.ClaudeUsage,
            Codex = config.Codex,
            GameFps = options.GameFps ?? config.GameFps,
            MaxCustomKeys = config.MaxCustomKeys,
            Ring = config.Ring,
            RingColor = config.RingColor,
            Handover = config.Handover,
            Sink = options.Sink,
        });
    }
}
