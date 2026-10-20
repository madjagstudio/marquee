using System.Collections.Concurrent;
using Marquee.Host.Updates;

namespace Marquee.App.Tests.TestSupport;

/// Stands in for starting a process: records the exe and command line and starts nothing, so no test runs msiexec.
/// With Failure set, throws it instead, as a program that can't be started does. Each start returns a FakeProcess that
/// runs until the test ends it.
sealed class FakeProcessStarter : IProcessStarter
{
    public ConcurrentQueue<(string FileName, string Arguments)> Started { get; } = new();

    public ConcurrentQueue<FakeProcess> Processes { get; } = new();

    public Exception? Failure { get; set; }

    /// Runs as a process is started, for tests that check what came before and after it.
    public Action? OnStart { get; set; }

    /// The process started last.
    public FakeProcess Last => Processes.Last();

    public IStartedProcess Start(string fileName, string arguments)
    {
        if (Failure is { } failure) throw failure;
        OnStart?.Invoke();
        var process = new FakeProcess();
        Processes.Enqueue(process);
        Started.Enqueue((fileName, arguments));
        return process;
    }
}

/// A started process that runs until Exit.
sealed class FakeProcess : IStartedProcess
{
    readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    volatile bool _disposed;

    public bool Disposed => _disposed;

    public void Exit(int exitCode) => _exited.TrySetResult(exitCode);

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => _exited.Task.WaitAsync(cancellationToken);

    public void Dispose() => _disposed = true;
}
