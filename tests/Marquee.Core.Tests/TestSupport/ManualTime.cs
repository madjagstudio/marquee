namespace Marquee.Core.Tests.TestSupport;

/// A clock that moves only when a test says so, for code that times things with GetTimestamp and GetElapsedTime.
sealed class ManualTime : TimeProvider
{
    long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}
