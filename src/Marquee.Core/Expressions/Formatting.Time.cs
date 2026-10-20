namespace Marquee.Core.Expressions;

/// The `time` helper: Unix seconds written as a local date and time, as `{now|time:HH:mm}` in a template or
/// `format: "time:HH:mm"` on a value widget. Unlike numbers, it follows the PC: its zone, and its culture's names and
/// short time, come from a TimeLocale.
public static partial class Formatting
{
    /// The helper's name. "time:PATTERN" gives it a .NET date and time pattern, such as "HH:mm" or "dddd".
    public const string TimeHelper = "time";

    /// The pattern the helper uses without one: the culture's short time, as in 6:48 PM or 18:48.
    public const string DefaultTimePattern = "t";

    // Newer ICU (CLDR 42 and later) puts a narrow no-break space before AM or PM, as in "5:30 PM", which a theme's
    // font may not have, and nothing falls back to another font for it: it would draw as a box. The time is written
    // with a no-break space instead: text fonts have it (it is in Latin-1), it keeps "5:30" and "PM" on one line when
    // text wraps, as the narrow one would, and it is as wide as the plain space older ICU writes there.
    const char NarrowNoBreakSpace = '\u202F';
    const char NoBreakSpace = '\u00A0';

    // The Unix seconds a DateTimeOffset can hold: 0001-01-01 00:00:00 to 9999-12-31 23:59:59 UTC.
    static readonly long MinUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    /// Unix seconds as a time in the locale's zone, written with a .NET date and time pattern in the locale's culture,
    /// with a no-break space for each narrow one. A value no date can hold (NaN, infinite, outside the years 1 to 9999,
    /// or outside the culture's calendar) is Missing.
    public static string Time(double unixSeconds, string pattern, TimeLocale locale)
    {
        if (!double.IsFinite(unixSeconds) || unixSeconds < MinUnixSeconds || unixSeconds >= MaxUnixSeconds + 1)
            return Missing;

        var instant = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Floor(unixSeconds * 1000));
        try
        {
            return TimeZoneInfo.ConvertTime(instant, locale.Zone).ToString(pattern, locale.Culture)
                .Replace(NarrowNoBreakSpace, NoBreakSpace);
        }
        catch (ArgumentOutOfRangeException)
        {
            // A culture whose calendar isn't Gregorian, such as Umm al-Qura, covers far fewer years.
            return Missing;
        }
    }

    /// Whether .NET can write a date and time with this pattern. Empty doesn't count: it would quietly mean "G".
    public static bool IsTimePattern(string pattern)
    {
        if (pattern.Length == 0) return false;
        try
        {
            _ = DateTimeOffset.UnixEpoch.ToString(pattern, Invariant);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// The pattern of a value widget's `format` that is the time helper ("time", or "time:" and a pattern), or null for
    /// any other format. A format with {0} is a composite format, as it always was, even one that starts "time:".
    static string? TimeFormatPattern(string format) =>
        format == TimeHelper ? DefaultTimePattern
        : format.StartsWith(TimeHelper + ":", StringComparison.Ordinal) && !format.Contains("{0") ? format[(TimeHelper.Length + 1)..]
        : null;
}
