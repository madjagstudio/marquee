using System.Collections.Concurrent;
using Marquee.Core.Logging;
using Marquee.Host.Resources;

namespace Marquee.Host;

/// The engine's theme events, queued where they happen (often under the engine's lock or a theme watcher's) and raised
/// later, with no lock held, so a handler may block, wait for a UI thread or call back into the engine without
/// deadlocking it. One thread raises them at a time, in the order they were queued.
sealed class EngineEvents(ILog log)
{
    readonly ConcurrentQueue<Action> _queue = new();
    int _raising;

    public void Enqueue(Action raise) => _queue.Enqueue(raise);

    /// Raises every queued event on this thread, unless another thread is raising them already: that one takes these
    /// too. A handler that throws is logged; the rest still run.
    public void Raise()
    {
        // Looping covers an event queued just as the other thread finished, after it last looked.
        while (!_queue.IsEmpty)
        {
            if (Interlocked.CompareExchange(ref _raising, 1, 0) != 0) return;
            try
            {
                while (_queue.TryDequeue(out var raise))
                {
                    try
                    {
                        raise();
                    }
                    catch (Exception ex)
                    {
                        log.Error(Strings.HandlerFailed(ex.Message));
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _raising, 0);
            }
        }
    }
}
