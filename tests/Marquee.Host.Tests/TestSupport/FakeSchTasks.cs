namespace Marquee.Host.Tests.TestSupport;

/// Stands in for schtasks.exe: records each command line and answers with `respond`, which runs during the call, so it
/// can look at a file the command line names while it still exists. Nothing here touches the real Task Scheduler.
sealed class FakeSchTasks(Func<IReadOnlyList<string>, SchTasksResult> respond) : ISchTasks
{
    public FakeSchTasks(int exitCode = 0, string output = "") : this(_ => new SchTasksResult(exitCode, output))
    {
    }

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public SchTasksResult Run(IReadOnlyList<string> arguments)
    {
        Calls.Add([.. arguments]);
        return respond(arguments);
    }
}
