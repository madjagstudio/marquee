using Marquee.Host.Updates;

namespace Marquee.Host.Tests.TestSupport;

/// Stands in for starting a process: records the exe and command line and starts nothing, so no test runs msiexec.
/// With `failure`, throws it instead, as a program that can't be started does. Each start returns a FakeProcess that
/// exits when the test says so.
sealed class FakeProcessStarter(Exception? failure = null) : IProcessStarter
{
    public List<(string FileName, string Arguments)> Started { get; } = [];

    public List<FakeProcess> Processes { get; } = [];

    public IStartedProcess Start(string fileName, string arguments)
    {
        if (failure is not null) throw failure;
        Started.Add((fileName, arguments));
        var process = new FakeProcess();
        Processes.Add(process);
        return process;
    }
}

/// A started process that runs until Exit.
sealed class FakeProcess : IStartedProcess
{
    readonly TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Disposed { get; private set; }

    public void Exit(int exitCode) => exited.TrySetResult(exitCode);

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => exited.Task.WaitAsync(cancellationToken);

    public void Dispose() => Disposed = true;
}
