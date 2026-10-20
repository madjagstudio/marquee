using System.Globalization;
using Microsoft.Win32;

namespace Marquee.Host;

/// Keeps the `time` helper on the PC's time zone and regional format while Marquee runs. .NET reads both once and keeps
/// them, so a tray running for days would show a new time zone, or a 24-hour clock chosen in Settings, only after a
/// restart. Windows says when either changes, and what .NET kept is cleared then, so the next frame's TimeLocale.Pc
/// reads them afresh. Daylight saving needs nothing: the kept zone carries its own rules. Clearing is safe while the
/// render thread reads them, and SystemEvents raises the notices on a thread of its own, made for a windowless process.
sealed class ClockSettingsWatch : IDisposable
{
    public ClockSettingsWatch()
    {
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Dispose()
    {
        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    /// The clock was set, or the time zone changed.
    internal static void OnTimeChanged(object? sender, EventArgs e) => TimeZoneInfo.ClearCachedData();

    /// The regional format (Windows' "Locale" preference) changed: its names, its short time, a 24-hour clock.
    internal static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Locale) CultureInfo.CurrentCulture.ClearCachedData();
    }
}
