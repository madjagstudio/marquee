using System.Globalization;

namespace Marquee.Core.Expressions;

/// Where and how the `time` helper writes a time: the time zone it converts Unix seconds to, and the culture whose day
/// and month names and short time it uses. The renderer uses the PC's own; tests pin both, so a clock draws the same
/// on every machine.
public sealed record TimeLocale(TimeZoneInfo Zone, CultureInfo Culture)
{
    /// The PC's time zone and regional format, as they are when asked.
    public static TimeLocale Pc => new(TimeZoneInfo.Local, CultureInfo.CurrentCulture);
}
