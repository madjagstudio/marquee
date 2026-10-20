namespace Marquee.Editor.Model.Tests.TestSupport;

/// A clock that moves only when a test says so, with timers that fire as it moves past them, so a PeriodicTimer made
/// with it ticks only on Advance. Timers fire on the test's thread, outside the clock's lock.
sealed class ManualTimers(DateTimeOffset start) : TimeProvider
{
    readonly object _gate = new();
    readonly List<ManualTimer> _timers = [];
    DateTimeOffset _now = start;

    public ManualTimers()
        : this(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    /// How many timers are waiting to fire.
    public int Pending
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

    /// Moves the clock on, firing each timer as often as its period fits into the time passed.
    public void Advance(TimeSpan by)
    {
        DateTimeOffset end;
        lock (_gate) end = _now + by;
        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(timer => timer.Due <= end).MinBy(timer => timer.Due);
                if (next is null)
                {
                    _now = end;
                    return;
                }

                _now = next.Due!.Value;
                next.Due = next.Period is { } period && period > TimeSpan.Zero ? _now + period : null;
            }

            next.Fire();
        }
    }

    sealed class ManualTimer(ManualTimers clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }
        public TimeSpan? Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan ? null : period;
            }

            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock._gate)
            {
                Due = null;
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
