namespace Marquee.Host.Tests.TestSupport;

/// A clock that moves only when a test says so. Timers made from it (Task.Delay and timed cancellation, given this
/// provider) fire on Advance, on the advancing thread, once their time has come. Thread-safe: the code under test makes
/// timers on its own threads.
sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    readonly object _gate = new();
    readonly List<ManualTimer> _timers = [];
    DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    // Timestamps in ticks of this clock, so elapsed times follow Advance too.
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    /// The timers that have yet to fire.
    public int PendingTimers
    {
        get
        {
            lock (_gate) return _timers.Count(timer => timer.Due is not null);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate) _timers.Add(timer);
        return timer;
    }

    /// Moves the clock on and fires every timer whose time has come, in order of their due times.
    public void Advance(TimeSpan by)
    {
        DateTimeOffset now;
        lock (_gate) now = _now += by;

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
                next = _timers.Where(timer => timer.Due <= now).MinBy(timer => timer.Due);
            if (next is null) return;
            next.Fire();
        }
    }

    sealed class ManualTimer(ManualTime time, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; private set; }
        TimeSpan _period = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (time._gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : time._now + dueTime;
                _period = period;
            }

            return true;
        }

        public void Fire()
        {
            lock (time._gate)
                Due = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : Due + _period;
            callback(state);
        }

        public void Dispose()
        {
            lock (time._gate)
            {
                Due = null;
                time._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
