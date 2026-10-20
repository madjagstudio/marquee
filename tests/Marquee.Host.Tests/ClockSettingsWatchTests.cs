using Marquee.Core.Expressions;
using Microsoft.Win32;

namespace Marquee.Host.Tests;

/// The clock follows a change of time zone or regional format made while Marquee runs: Windows' notices clear what .NET
/// keeps of them, so the next frame's TimeLocale.Pc reads them afresh. The handlers are raised directly, as Windows
/// would raise them.
public class ClockSettingsWatchTests
{
    [Fact]
    public void A_time_change_reads_the_time_zone_afresh()
    {
        var before = TimeLocale.Pc.Zone;

        ClockSettingsWatch.OnTimeChanged(null, EventArgs.Empty);

        Assert.NotSame(before, TimeLocale.Pc.Zone);
    }

    [Fact]
    public void A_regional_format_change_reads_the_culture_afresh()
    {
        var before = TimeLocale.Pc.Culture;

        ClockSettingsWatch.OnUserPreferenceChanged(null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.Locale));

        Assert.NotSame(before, TimeLocale.Pc.Culture);
    }

    [Fact]
    public void Other_preference_changes_keep_the_culture()
    {
        var before = TimeLocale.Pc.Culture;

        ClockSettingsWatch.OnUserPreferenceChanged(null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.Desktop));

        Assert.Same(before, TimeLocale.Pc.Culture);
    }
}
