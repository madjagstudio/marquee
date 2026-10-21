namespace Marquee.Editor.Model.Tests.TestSupport;

/// A UI thread for tests: work posted to it, from any thread, waits until the test runs it on its own.
sealed class ManualContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback Callback, object? State)> _posted = new();

    public int Pending
    {
        get
        {
            lock (_posted) return _posted.Count;
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_posted) _posted.Enqueue((d, state));
    }

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("The editor only posts.");

    /// Runs what was posted, and what that posts in turn, until nothing is left. Returns how many ran.
    public int RunPending()
    {
        var ran = 0;
        while (true)
        {
            (SendOrPostCallback Callback, object? State) next;
            lock (_posted)
            {
                if (!_posted.TryDequeue(out next)) return ran;
            }

            next.Callback(next.State);
            ran++;
        }
    }
}
