namespace Marquee.Editor.Tests.TestSupport;

/// A clock whose time, and whose timers, move only when a test says so. Advance fires every timer that falls due on the
/// way, in time order, on the test's own thread, so what a timer does has happened when Advance returns. Timers may be
/// changed from other threads (a FileSystemWatcher's), which Armed lets a test wait for.
sealed class FakeClock : TimeProvider
{
    readonly object _gate = new();
    readonly List<Timer> _timers = [];
    DateTimeOffset _now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    /// How many timers are waiting to fire.
    public int Armed
    {
        get
        {
            lock (_gate) return _timers.Count(timer => timer.Due is not null);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        lock (_gate) _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset end;
        lock (_gate) end = _now + by;
        while (true)
        {
            Timer? next;
            lock (_gate)
            {
                next = _timers.Where(timer => timer.Due <= end).MinBy(timer => timer.Due);
                if (next is null)
                {
                    _now = end;
                    return;
                }

                _now = next.Due!.Value;
                next.Due = next.Period > TimeSpan.Zero ? _now + next.Period : null;
            }

            next.Fire();
        }
    }

    /// Waits, in real time, until a timer is armed: for one a FileSystemWatcher's event arms on its own thread.
    public bool WaitUntilArmed(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (Armed > 0) return true;
            Thread.Sleep(10);
        }

        return Armed > 0;
    }

    sealed class Timer(FakeClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }

        public TimeSpan Period { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                if (!clock._timers.Contains(this)) return false;
                Period = period;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                return true;
            }
        }

        public void Dispose()
        {
            lock (clock._gate) clock._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
