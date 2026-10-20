namespace Marquee.Host.Tests.TestSupport;

/// A SynchronizationContext like a UI thread's while that thread is blocked: work posted to it is queued and never run.
/// A continuation that comes back to the context it started on then never runs, which is how a synchronous call over
/// async code deadlocks on the WPF Dispatcher.
sealed class QueueOnlyContext : SynchronizationContext
{
    int _posted;

    /// Callbacks posted so far, none of which ran.
    public int Posted => Volatile.Read(ref _posted);

    public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posted);

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("a blocked UI thread can't run anything sent to it");

    /// Runs `action` on a thread of its own with this context installed, and says whether it finished within `limit`.
    /// A deadlocked action is left blocked on its background thread.
    public bool Run(Action action, TimeSpan limit)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }) { IsBackground = true, Name = "Marquee test UI thread" };
        thread.Start();
        if (!thread.Join(limit)) return false;
        if (failure is not null) throw new InvalidOperationException("the action failed on the context's thread", failure);
        return true;
    }
}
