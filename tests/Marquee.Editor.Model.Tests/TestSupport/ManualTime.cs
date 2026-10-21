namespace Marquee.Editor.Model.Tests.TestSupport;

/// A clock that moves only when a test says so.
sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset _now = start;

    public ManualTime()
        : this(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
