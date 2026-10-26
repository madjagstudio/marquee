using System.Runtime.InteropServices;

namespace Marquee.Host;

/// What Task Scheduler's own API does that schtasks can't: run a task in a given signed-in session, and delete a task
/// folder. Tests put a fake in its place, so no test runs a real task or changes a real folder.
public interface ITaskRunner
{
    /// Throws COMException with Task Scheduler's reason when it can't (no such task, no rights to run it).
    void RunInSession(string taskName, int sessionId);

    /// Deletes the task folder `folder` (such as \Marquee) if it holds no task and no folder. False when it isn't there
    /// or holds something, which stays; throws COMException with Task Scheduler's reason when it can't be deleted.
    bool DeleteFolderIfEmpty(string folder);
}

/// Task Scheduler's own API, through its scripting object. schtasks /Run can't say which session: with
/// TASK_RUN_USE_SESSION_ID and no user, IRegisteredTask.RunEx starts the task "interactively as the user who is logged
/// on to the specified session", with the task's run level, so a task that runs with highest privileges starts elevated
/// there. And schtasks deletes tasks but never the folder they were in. Running a task and deleting a folder need the
/// rights to them, which the installer (SYSTEM) and administrators have; the logon task's descriptor (LogonTask.Sddl)
/// lets the Users group run it too.
public sealed class TaskRunner : ITaskRunner
{
    const int TASK_RUN_USE_SESSION_ID = 0x4;
    const int TASK_ENUM_HIDDEN = 0x1;
    // HRESULTs from Windows error codes.
    const int ERROR_FILE_NOT_FOUND = unchecked((int)0x80070002);
    const int ERROR_PATH_NOT_FOUND = unchecked((int)0x80070003);
    const int ERROR_DIR_NOT_EMPTY = unchecked((int)0x80070091);

    public void RunInSession(string taskName, int sessionId) => WithService(service =>
    {
        dynamic task = service.GetFolder(@"\").GetTask(taskName);
        // No parameters for the action, no user: the session's own user.
        task.RunEx(null, TASK_RUN_USE_SESSION_ID, sessionId, null);
        return true;
    });

    public bool DeleteFolderIfEmpty(string folder)
    {
        try
        {
            return WithService(service =>
            {
                dynamic found = service.GetFolder(folder);
                if (found.GetTasks(TASK_ENUM_HIDDEN).Count > 0 || found.GetFolders(0).Count > 0) return false;
                service.GetFolder(@"\").DeleteFolder(folder.TrimStart('\\'), 0);
                return true;
            });
        }
        // By its code, which is the same in every language: gone already, or something was put in it just now.
        catch (COMException ex) when (ex.HResult is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND or ERROR_DIR_NOT_EMPTY)
        {
            return false;
        }
    }

    /// Connects to this PC's Task Scheduler, as this process's user, for `use`.
    static bool WithService(Func<dynamic, bool> use)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
            dynamic service = Activator.CreateInstance(type)!;
            try
            {
                service.Connect();
                return use(service);
            }
            finally
            {
                Marshal.FinalReleaseComObject(service);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // .NET turns some of Task Scheduler's error codes into these (a missing task into FileNotFoundException);
            // whatever the code, it is Task Scheduler's error, with its own message and code.
            throw new COMException(ex.Message, ex.HResult);
        }
    }
}
