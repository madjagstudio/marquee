using System.Globalization;
using Marquee.Core.Expressions;

namespace Marquee.Core.Tests.TestSupport;

/// A pinned time zone and culture for the `time` helper, so its tests don't depend on the machine's own settings.
static class TestClock
{
    /// UTC+05:30 without daylight saving. TestSensors.Time (12:00 UTC) is 17:30 there, on Friday 16 October 2026.
    public static readonly TimeZoneInfo Zone = Fixed(TimeSpan.FromMinutes(330));

    /// The pinned zone, in the given culture (US English unless said).
    public static TimeLocale Locale(string culture = "en-US", TimeZoneInfo? zone = null) =>
        new(zone ?? Zone, CultureInfo.GetCultureInfo(culture));

    /// A zone at a fixed offset from UTC, with no daylight saving.
    public static TimeZoneInfo Fixed(TimeSpan offset)
    {
        var name = $"Test {offset:hh\\:mm}";
        return TimeZoneInfo.CreateCustomTimeZone(name, offset, name, name);
    }
}
