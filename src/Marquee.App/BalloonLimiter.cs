namespace Marquee.App;

/// At most one balloon of each kind per `every`, so a theme that fails on every save, or a screen that keeps failing,
/// doesn't bury the desktop in notifications, while one kind never holds back another: a broken config.yaml at logon
/// doesn't hide that the theme didn't load, or that taking the screen from L-Connect failed. A kind is any key; the
/// tray uses the balloon's title. Thread-safe: engine events arrive on engine threads.
sealed class BalloonLimiter(TimeSpan every, Func<DateTime> utcNow)
{
    readonly Dictionary<string, DateTime> _last = [];

    /// True, and the interval for `kind` starts again, when a balloon of that kind may show now.
    public bool TryTake(string kind)
    {
        lock (_last)
        {
            var now = utcNow();
            if (_last.TryGetValue(kind, out var last) && now - last < every) return false;
            _last[kind] = now;
            return true;
        }
    }
}
