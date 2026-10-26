using System.ComponentModel;
using System.Runtime.InteropServices;
using Marquee.Core.Processes;
using Marquee.Host;

namespace Marquee.App.Tests;

/// The handoff through a fake Task Scheduler: no test here runs a real task.
public class LogonTaskHandoffTests
{
    const string Tray = @"C:\Program Files\Marquee\MarqueeTray.exe";

    [Theory]
    [InlineData(ProcessElevation.TokenElevationTypeLimited, @"c:\program files\marquee\marqueetray.exe", true)]
    [InlineData(ProcessElevation.TokenElevationTypeLimited, @"C:\dev\Marquee\bin\Debug\MarqueeTray.exe", false)]
    [InlineData(ProcessElevation.TokenElevationTypeLimited, null, false)]
    [InlineData(ProcessElevation.TokenElevationTypeLimited, "", false)]
    [InlineData(ProcessElevation.TokenElevationTypeLimited, "   ", false)]
    [InlineData(ProcessElevation.TokenElevationTypeLimited, "C:\\a\0b", false)]
    [InlineData(ProcessElevation.TokenElevationTypeDefault, Tray, false)]
    [InlineData(ProcessElevation.TokenElevationTypeFull, Tray, false)]
    public void Only_a_filtered_token_with_a_task_for_this_exe_needs_a_handoff(int elevationType, string? taskExePath, bool expected)
    {
        var task = new LogonTaskStatus(true, taskExePath, Enabled: true, StartsOnDemand: true, RunsHighest: true);

        Assert.Equal(expected, LogonTaskHandoff.ShouldHandOff(elevationType, task, Tray));
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void A_task_must_be_installed_enabled_start_on_demand_and_run_highest(bool installed, bool enabled, bool startsOnDemand, bool runsHighest)
    {
        var task = new LogonTaskStatus(installed, Tray, enabled, startsOnDemand, runsHighest);

        Assert.False(LogonTaskHandoff.ShouldHandOff(ProcessElevation.TokenElevationTypeLimited, task, Tray));
    }

    [Fact]
    public void An_unreadable_task_never_gets_a_handoff()
    {
        Assert.False(LogonTaskHandoff.ShouldHandOff(ProcessElevation.TokenElevationTypeLimited, null, Tray));
    }

    [Fact]
    public void The_task_status_comes_from_its_xml()
    {
        var schtasks = new FakeSchTasks(_ => new SchTasksResult(0, LogonTask.Xml(Tray)));

        Assert.Equal(new LogonTaskStatus(true, Tray, Enabled: true, StartsOnDemand: true, RunsHighest: true), LogonTaskHandoff.Task(schtasks));
    }

    [Fact]
    public void A_missing_task_is_not_installed()
    {
        var schtasks = new FakeSchTasks(_ => new SchTasksResult(1, $"ERROR: {new Win32Exception(2).Message}.{Environment.NewLine}"));

        Assert.Equal(new LogonTaskStatus(false, null), LogonTaskHandoff.Task(schtasks));
    }

    [Fact]
    public void A_task_query_timeout_returns_no_status()
    {
        var schtasks = new FakeSchTasks(_ => throw new TimeoutException("schtasks timed out."));

        Assert.Null(LogonTaskHandoff.Task(schtasks));
    }

    [Fact]
    public void It_starts_the_logon_task_in_the_given_session()
    {
        var runner = new FakeTaskRunner();

        Assert.True(LogonTaskHandoff.TryStart(runner, 3, out var failure));

        Assert.Null(failure);
        Assert.Equal((@"\Marquee\Marquee", 3), Assert.Single(runner.Runs));
    }

    [Fact]
    public void A_task_scheduler_error_returns_its_message()
    {
        var runner = new FakeTaskRunner(new COMException("Access is denied."));

        Assert.False(LogonTaskHandoff.TryStart(runner, 3, out var failure));

        Assert.Equal("Access is denied.", failure);
    }

    [Fact]
    public void An_unexpected_error_propagates()
    {
        var error = new InvalidOperationException("Unexpected failure.");
        var runner = new FakeTaskRunner(error);

        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => LogonTaskHandoff.TryStart(runner, 3, out _)));
    }

    sealed class FakeSchTasks(Func<IReadOnlyList<string>, SchTasksResult> respond) : ISchTasks
    {
        public SchTasksResult Run(IReadOnlyList<string> arguments) => respond(arguments);
    }

    /// Records the task and session, or throws `error`, without asking Task Scheduler anything.
    sealed class FakeTaskRunner(Exception? error = null) : ITaskRunner
    {
        public List<(string Task, int Session)> Runs { get; } = [];

        public void RunInSession(string taskName, int sessionId)
        {
            if (error is not null) throw error;
            Runs.Add((taskName, sessionId));
        }

        public bool DeleteFolderIfEmpty(string folder) => false;
    }
}
