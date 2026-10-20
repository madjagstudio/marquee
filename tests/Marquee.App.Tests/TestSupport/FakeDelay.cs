namespace Marquee.App.Tests.TestSupport;

/// Stands in for Task.Delay, so the daily update timer never really waits: it records each wait asked for, and the
/// test ends the latest with Elapse. A cancelled wait ends cancelled, as Task.Delay's does.
sealed class FakeDelay
{
    readonly object _gate = new();
    readonly List<TimeSpan> _requested = [];
    TaskCompletionSource? _pending;

    /// Every wait asked for, in order.
    public IReadOnlyList<TimeSpan> Requested
    {
        get
        {
            lock (_gate) return [.. _requested];
        }
    }

    public Task Wait(TimeSpan due, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
        lock (_gate)
        {
            _requested.Add(due);
            _pending = done;
        }

        return done.Task;
    }

    /// Waits (for real, up to Wait.Timeout) until `count` waits have been asked for.
    public bool WaitForRequests(int count) => TestSupport.Wait.Until(() => Requested.Count >= count);

    /// The latest wait is over.
    public void Elapse()
    {
        TaskCompletionSource? pending;
        lock (_gate) (pending, _pending) = (_pending, null);
        pending?.TrySetResult();
    }
}
