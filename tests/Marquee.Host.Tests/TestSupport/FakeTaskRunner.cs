namespace Marquee.Host.Tests.TestSupport;

/// Stands in for Task Scheduler: records each task it is asked to run and the session, or throws `error`. It has no
/// folders to delete.
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

/// Stands in for the PC's signed-in sessions: each user's SID and the session they are signed in to.
sealed class FakeSessions(params (string UserSid, int SessionId)[] sessions) : IUserSessions
{
    public IReadOnlyList<int> SessionsOf(string userSid) =>
        [.. sessions.Where(session => session.UserSid == userSid).Select(session => session.SessionId)];
}
