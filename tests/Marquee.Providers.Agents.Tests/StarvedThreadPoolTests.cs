using System.Text;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

/// Blocks the thread pool for the whole run, so these tests must not run beside any others.
[CollectionDefinition(nameof(StarvedThreadPoolTests), DisableParallelization = true)]
public sealed class StarvedThreadPoolCollection;

/// Running a child must not depend on the thread pool: on a busy PC (or a loaded CI runner) pool work can wait seconds to
/// start, and a child's output read on a pool task after it exited came back empty, so a `/usage` poll or a status line
/// could be lost. Each test fills the pool with blocked work first, so nothing queued to it runs until the test ends.
[Collection(nameof(StarvedThreadPoolTests))]
public sealed class StarvedThreadPoolTests : IDisposable
{
    static readonly string PowerShellExe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    readonly TempDir _dir = new();
    readonly ManualResetEventSlim _release = new();
    readonly int _maxWorkers;
    readonly int _maxIo;

    public StarvedThreadPoolTests()
    {
        // The pool adds threads when its work blocks, so cap it at its minimum for the test, then occupy every thread.
        // The test itself runs on a pool thread, so not every blocker starts: some left waiting in the queue is the
        // proof that the pool is full.
        ThreadPool.GetMaxThreads(out _maxWorkers, out _maxIo);
        ThreadPool.GetMinThreads(out var workers, out var io);
        Assert.True(ThreadPool.SetMaxThreads(workers, io), "Could not cap the thread pool");
        for (var i = 0; i < workers + 8; i++)
            ThreadPool.UnsafeQueueUserWorkItem(_ => _release.Wait(), null);
        Assert.True(SpinWait.SpinUntil(() => ThreadPool.PendingWorkItemCount > 0 && ThreadPool.ThreadCount >= workers,
            TimeSpan.FromSeconds(30)), "The thread pool did not fill up");
    }

    public void Dispose()
    {
        ThreadPool.SetMaxThreads(_maxWorkers, _maxIo);
        _release.Set();
        _dir.Dispose();
    }

    static string[] PowerShell(string script) => ["-NoProfile", "-NonInteractive", "-Command", script];

    [Fact]
    public void RunProcess_returns_the_output_while_the_pool_is_busy()
    {
        var result = ClaudeCli.RunProcess(PowerShellExe, PowerShell("Write-Output 'still here'"), _dir.Path, TimeSpan.FromSeconds(60));

        Assert.Equal((0, "still here"), (result.ExitCode, result.Output.Trim()));
    }

    [Fact]
    public void UnelevatedProcess_returns_the_output_while_the_pool_is_busy()
    {
        var result = UnelevatedProcess.RunWithOwnToken(PowerShellExe, PowerShell("Write-Output 'still here'"), _dir.Path,
            TimeSpan.FromSeconds(60));

        Assert.Equal((0, "still here"), (result.ExitCode, result.Output.Trim()));
    }

    [Fact]
    public void A_shell_command_gets_its_input_and_returns_its_output_while_the_pool_is_busy()
    {
        var result = ShellCommand.PowerShell.Run("$input | ForEach-Object { \"got $_\" }", Encoding.UTF8.GetBytes("ping\n"),
            TimeSpan.FromSeconds(60));

        Assert.False(result.TimedOut);
        Assert.Equal("got ping", Encoding.UTF8.GetString(result.Output).Trim());
    }
}
