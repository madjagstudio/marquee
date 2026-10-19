using Marquee.Core.Config;

namespace Marquee.App;

/// A fresh install has no config.yaml, so nothing names a theme. The tray shows an installed example meanwhile, and
/// once, the first time, a balloon saying where to pick another theme and the orientation.
static class FirstRun
{
    /// The theme a config.yaml that names none shows: landscape, as the screen is mounted unless config.yaml says
    /// otherwise.
    public const string DefaultTheme = "example-landscape";

    /// The file in the data folder that says the hint was shown.
    public const string HintMarker = "first-run-hint-shown";

    /// `config`, with DefaultTheme when it names no theme. Applied to every read of config.yaml, so deleting the
    /// `theme:` line later goes back to the example rather than to nothing.
    public static MarqueeConfig WithDefaultTheme(MarqueeConfig config) =>
        config.Theme is null ? config with { Theme = DefaultTheme } : config;

    /// True the first time only: it writes HintMarker in `dataFolder` (MarqueePaths.ProcessDataFolder, which only
    /// administrators can write when the tray is elevated, so nothing can plant a link there). When the marker can't
    /// be written this is false as well, so a broken folder doesn't bring the hint back at every start.
    public static bool ClaimHint(string dataFolder)
    {
        var marker = Path.Combine(dataFolder, HintMarker);
        try
        {
            if (File.Exists(marker)) return false;
            Directory.CreateDirectory(dataFolder);
            using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
