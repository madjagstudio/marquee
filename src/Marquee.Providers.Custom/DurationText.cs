using System.Globalization;

namespace Marquee.Providers.Custom;

/// Durations as people type them: "30" or "30s" (seconds), "1.5m", "2h", "1d".
public static class DurationText
{
    /// The longest ttl a pushed value may have.
    public static readonly TimeSpan Max = TimeSpan.FromDays(365);

    /// True for a positive duration of at most a year.
    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();
        var (number, unit) = char.IsAsciiLetter(text[^1]) ? (text[..^1], text[^1]) : (text, 's');
        var seconds = char.ToLowerInvariant(unit) switch
        {
            's' => 1,
            'm' => 60,
            'h' => 3600,
            'd' => 86400,
            _ => 0,
        };
        if (seconds == 0 || !double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return false;

        return FromSeconds(value * seconds, out duration);
    }

    /// True for a positive number of seconds up to Max.
    public static bool FromSeconds(double seconds, out TimeSpan duration)
    {
        duration = default;
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > Max.TotalSeconds) return false;
        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }
}
