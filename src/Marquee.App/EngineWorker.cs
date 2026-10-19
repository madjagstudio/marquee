using System.Collections.Concurrent;

namespace Marquee.App;

/// The one thread the tray's engine work runs on. Engine calls load themes and wait for render threads, so they must
/// never run on the UI thread; running them on one thread of their own, in the order they were asked for, also means a
/// quick Pause then Resume can't finish the other way round.
sealed class EngineWorker : IDisposable
{
    readonly BlockingCollection<(Action Work, TaskCompletionSource Done)> _queue = new();
    readonly Thread _thread;

    public EngineWorker()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Marquee tray engine" };
        _thread.Start();
    }

    /// Queues `work`. The task completes when it has run, faulted with what it threw; canceled after Dispose. It
    /// completes off this thread, so a continuation never runs on the worker by accident.
    public Task Run(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add((work, done));
        }
        catch (InvalidOperationException)
        {
            done.SetCanceled(); // disposed: the tray is quitting
        }

        return done.Task;
    }

    /// Lets the queued work finish (for at most 30 seconds, about what stopping the engine can take), then stops.
    public void Dispose()
    {
        _queue.CompleteAdding();
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(30));
    }

    void Loop()
    {
        foreach (var (work, done) in _queue.GetConsumingEnumerable())
        {
            try
            {
                work();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }
    }
}
