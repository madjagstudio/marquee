namespace Marquee.Host.SensorFeed;

/// At most `limit` connections in any `window`: past that, the next one waits until the oldest of the last `limit` is a
/// window old. Elapsed times come from the clock's timestamps, so a change of the wall clock moves nothing. One pipe's
/// loop owns it; not thread-safe.
sealed class ConnectionRate(TimeProvider time, int limit, TimeSpan window)
{
    readonly Queue<long> _recent = new();

    /// How long until another connection may be taken; zero if one may now.
    public TimeSpan Wait()
    {
        while (_recent.Count > 0 && time.GetElapsedTime(_recent.Peek()) >= window) _recent.Dequeue();
        return _recent.Count < limit ? TimeSpan.Zero : window - time.GetElapsedTime(_recent.Peek());
    }

    /// Returns once another connection may be taken.
    public async Task WaitTurn(CancellationToken stop)
    {
        for (var wait = Wait(); wait > TimeSpan.Zero; wait = Wait())
            await Task.Delay(wait, time, stop);
    }

    /// A connection was taken now.
    public void Record() => _recent.Enqueue(time.GetTimestamp());
}
