using System.ComponentModel;
using Marquee.Host;

namespace Marquee.Cli.Tests.TestSupport;

/// Stands in for Task Scheduler: records each task run and its session, or throws `error`, and each task folder it is
/// asked to delete, or throws `folderError`. No real task runs and no real folder goes.
sealed class FakeTaskRunner(Exception? error = null, Exception? folderError = null) : ITaskRunner
{
    public List<(string Task, int Session)> Runs { get; } = [];

    public List<string> DeletedFolders { get; } = [];

    public void RunInSession(string taskName, int sessionId)
    {
        if (error is not null) throw error;
        Runs.Add((taskName, sessionId));
    }

    public bool DeleteFolderIfEmpty(string folder)
    {
        if (folderError is not null) throw folderError;
        DeletedFolders.Add(folder);
        return true;
    }
}

/// Stands in for the PC's signed-in sessions: each user's SID and the session they are signed in to.
sealed class FakeSessions(params (string UserSid, int SessionId)[] sessions) : IUserSessions
{
    public IReadOnlyList<int> SessionsOf(string userSid) =>
        [.. sessions.Where(session => session.UserSid == userSid).Select(session => session.SessionId)];
}

/// Stands in for the running trays: each quits when asked, or doesn't hear and is ended, or can't be ended. Nothing here
/// signals or ends a real process.
sealed class FakeTrayProcesses(params FakeTrayProcesses.Kind[] trays) : ITrayProcesses
{
    public enum Kind { QuitsWhenAsked, Deaf, Unkillable }

    public string? AskedFor { get; private set; }

    public IReadOnlyList<ITray> Running(string trayExePath)
    {
        AskedFor = trayExePath;
        return [.. trays.Select((kind, i) => new Tray(i + 1, kind))];
    }

    sealed class Tray(int processId, Kind kind) : ITray
    {
        public int ProcessId => processId;

        public bool AskToQuit() => kind == Kind.QuitsWhenAsked;

        public bool WaitForExit(TimeSpan timeout) => kind == Kind.QuitsWhenAsked;

        public void Kill()
        {
            if (kind == Kind.Unkillable) throw new Win32Exception(5);
        }

        public void Dispose()
        {
        }
    }
}
