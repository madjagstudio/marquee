using Marquee.Core.Config;

namespace Marquee.App;

/// A fresh install has no config.yaml, so nothing names a theme. The tray shows an installed example meanwhile, and
/// once, the first time, a balloon saying where to pick another theme and the orientation. It also offers the L-Connect
/// handover once, the first time L-Connect holds the screen, to a user who hasn't set `lconnect.handover`.
static class FirstRun
{
    /// The theme a config.yaml that names none shows: landscape, as the screen is mounted unless config.yaml says
    /// otherwise.
    public const string DefaultTheme = "example-landscape";

    /// The file in the data folder that says the hint was shown.
    public const string HintMarker = "first-run-hint-shown";

    /// The file in the data folder that says the handover was offered.
    public const string HandoverOfferMarker = "handover-offer-shown";

    /// `config`, with DefaultTheme when it names no theme. Applied to every read of config.yaml, so deleting the
    /// `theme:` line later goes back to the example rather than to nothing.
    public static MarqueeConfig WithDefaultTheme(MarqueeConfig config) =>
        config.Theme is null ? config with { Theme = DefaultTheme } : config;

    /// True the first time only: it writes HintMarker in `dataFolder` (MarqueePaths.ProcessDataFolder, which only
    /// administrators can write when the tray is elevated, so nothing can plant a link there). When the marker can't
    /// be written this is false as well, so a broken folder doesn't bring the hint back at every start.
    public static bool ClaimHint(string dataFolder) => Claim(dataFolder, HintMarker);

    /// As ClaimHint, for the handover's one-time offer, with HandoverOfferMarker.
    public static bool ClaimHandoverOffer(string dataFolder) => Claim(dataFolder, HandoverOfferMarker);

    static bool Claim(string dataFolder, string name)
    {
        var marker = Path.Combine(dataFolder, name);
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
