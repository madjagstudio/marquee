namespace Marquee.Host.Tests.TestSupport;

/// One-shot timers that fire only when a test says so: Schedule stands in for a System.Threading.Timer due once after a
/// delay, and disposing what it returns cancels it, as disposing the timer does. Thread-safe: the code under test may
/// schedule from its own threads.
sealed class FakeTimers
{
    readonly object _gate = new();
    readonly List<Timer> _timers = [];

    /// Every timer scheduled so far, fired, cancelled or still pending, in order.
    public IReadOnlyList<Timer> All
    {
        get
        {
            lock (_gate) return [.. _timers];
        }
    }

    /// The timers neither fired nor cancelled.
    public IReadOnlyList<Timer> Pending => All.Where(timer => timer.IsPending).ToList();

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var timer = new Timer(delay, callback);
        lock (_gate) _timers.Add(timer);
        return timer;
    }

    public sealed class Timer(TimeSpan delay, Action callback) : IDisposable
    {
        int _state; // 0 pending, 1 fired, 2 cancelled

        public TimeSpan Delay { get; } = delay;

        public bool IsPending => Volatile.Read(ref _state) == 0;

        public bool IsCancelled => Volatile.Read(ref _state) == 2;

        /// Runs the callback on the calling thread, as the timer's thread would, unless it was cancelled or fired.
        public void Fire()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0) callback();
        }

        /// Runs the callback even though the timer was cancelled: a real timer's callback that had already started when
        /// it was disposed, and got as far as the code under test only afterwards.
        public void FireAfterCancel() => callback();

        public void Dispose() => Interlocked.CompareExchange(ref _state, 2, 0);
    }
}
