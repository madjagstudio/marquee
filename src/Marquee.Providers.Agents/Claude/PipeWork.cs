namespace Marquee.Providers.Agents.Claude;

/// Reading a child's output and writing its input, each on a thread of its own rather than the thread pool. Pool work
/// can wait seconds to start on a busy PC or a loaded CI runner, and the caller only waits a moment for the output once
/// the child has exited: a pool task that hadn't started by then returned nothing, so a `/usage` poll or a status line
/// was lost. A dedicated thread starts at once.
static class PipeWork
{
    public static Task Start(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
}
