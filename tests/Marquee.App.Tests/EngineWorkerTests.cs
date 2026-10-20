using Marquee.App.Tests.TestSupport;

namespace Marquee.App.Tests;

public class EngineWorkerTests
{
    [Fact]
    public async Task Work_runs_in_order_on_one_thread_that_is_not_the_callers()
    {
        using var worker = new EngineWorker();
        var threads = new List<int>();
        var order = new List<int>();

        var tasks = Enumerable.Range(0, 5).Select(i => worker.Run(() =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            order.Add(i);
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(Wait.Timeout);
        Assert.Equal([0, 1, 2, 3, 4], order);
        Assert.Single(threads.Distinct());
        Assert.NotEqual(Environment.CurrentManagedThreadId, threads[0]);
    }

    [Fact]
    public async Task A_failure_faults_its_own_task_and_the_worker_carries_on()
    {
        using var worker = new EngineWorker();

        var failed = worker.Run(() => throw new InvalidOperationException("boom"));
        var next = worker.Run(() => { });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => failed.WaitAsync(Wait.Timeout));
        Assert.Equal("boom", error.Message);
        await next.WaitAsync(Wait.Timeout);
    }

    [Fact]
    public void Dispose_finishes_the_work_queued_before_it_and_refuses_more()
    {
        var worker = new EngineWorker();
        var ran = false;
        var queued = worker.Run(() =>
        {
            Thread.Sleep(50);
            ran = true;
        });

        worker.Dispose();

        Assert.True(queued.IsCompletedSuccessfully);
        Assert.True(ran);
        Assert.True(worker.Run(() => { }).IsCanceled);
    }
}
