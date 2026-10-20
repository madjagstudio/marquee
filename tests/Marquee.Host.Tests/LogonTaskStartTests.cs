using System.Runtime.InteropServices;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

/// Starting the logon task now, as the installer does when it finishes, through fake sessions and a fake Task
/// Scheduler: no test here runs a real task.
public class LogonTaskStartTests
{
    const string User = "S-1-5-21-1000-2000-3000-1001";

    [Fact]
    public void Start_runs_the_task_in_each_session_the_user_is_signed_in_to()
    {
        var runner = new FakeTaskRunner();

        var sessions = LogonTask.Start(User, new FakeSessions((User, 1), ("S-1-5-21-1000-2000-3000-1002", 2), (User, 3)), runner);

        Assert.Equal([1, 3], sessions);
        Assert.Equal([(@"\Marquee\Marquee", 1), (@"\Marquee\Marquee", 3)], runner.Runs);
    }

    [Fact]
    public void Start_runs_nothing_when_the_user_is_not_signed_in()
    {
        var runner = new FakeTaskRunner();

        var sessions = LogonTask.Start(User, new FakeSessions(("S-1-5-21-1000-2000-3000-1002", 2)), runner);

        Assert.Empty(sessions);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void A_task_scheduler_error_becomes_a_logon_task_error_with_its_message()
    {
        var runner = new FakeTaskRunner(new COMException("The system cannot find the file specified.", unchecked((int)0x80070002)));

        var error = Assert.Throws<LogonTaskException>(() => LogonTask.Start(User, new FakeSessions((User, 1)), runner));

        Assert.Equal("Task Scheduler could not start the task: The system cannot find the file specified.", error.Message);
    }

    [Fact]
    public void The_real_sessions_hold_no_one_for_a_made_up_user()
    {
        Assert.Empty(new UserSessions().SessionsOf(User));
    }
}
