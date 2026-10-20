using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Marquee.Core;
using Marquee.Core.Processes;
using Marquee.Host;
using Marquee.Host.Updates;

/// `marquee task install [--tray <file>] | remove [--updates] | status | start [--user <SID>] | stop`: the elevated
/// logon task that starts the tray. The installer runs all but status: install and start when it installs, stop before
/// it replaces or removes files, and remove --updates when it uninstalls; a user can run them from an administrator
/// terminal. Status needs no elevation, and exits 0 when the task is installed and 1 when it isn't, so a script can test
/// for it.
static class TaskCommand
{
    /// What the command needs from the PC, so tests can fake it: whether Marquee runs elevated, schtasks, marquee.exe's
    /// folder (where the installer puts the tray, and where the task's XML is written), the admin-only folders a tray
    /// should be under, the user running this, their sessions and Task Scheduler (for start), the running trays (for
    /// stop), the folder of downloaded updates (made by install, emptied by remove --updates) and how a folder under
    /// %ProgramData%\Marquee is made admin-only (SecureFolder.Ensure).
    internal sealed record Machine(Func<bool> IsElevated, ISchTasks SchTasks, string MarqueeFolder, IReadOnlyList<string> AdminFolders,
        string UserSid, IUserSessions Sessions, ITaskRunner Tasks, ITrayProcesses Trays, string UpdatesFolder, Action<string> SecureFolder)
    {
        public static Machine Real => new(ProcessElevation.IsCurrentProcessElevated, new SchTasks(), AppContext.BaseDirectory,
        [
            .. new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .Select(Environment.GetFolderPath)
                .Where(folder => folder.Length > 0),
        ], CurrentUserSid(), new UserSessions(), new TaskRunner(), new TrayProcesses(), MarqueePaths.Updates, Marquee.Core.Files.SecureFolder.Ensure);

        static string CurrentUserSid()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User!.Value;
        }
    }

    /// Null means the arguments were wrong and the caller should print usage.
    public static int? Run(string[] args) => Run(args, Machine.Real);

    internal static int? Run(string[] args, Machine machine) => args switch
    {
        ["install"] => Install(null, machine),
        ["install", "--tray", var tray] when tray.Trim().Length > 0 => Install(tray, machine),
        ["remove"] => Remove(updates: false, machine),
        ["remove", "--updates"] => Remove(updates: true, machine),
        ["status"] => Status(machine),
        ["start"] => Start(machine.UserSid, machine),
        ["start", "--user", var user] when IsSid(user) => Start(user, machine),
        ["stop"] => Stop(machine),
        _ => null,
    };

    static int Install(string? tray, Machine machine)
    {
        if (!machine.IsElevated())
        {
            Console.Error.WriteLine(Strings.TaskNeedsAdministrator("marquee task install"));
            return 1;
        }

        var exe = Path.GetFullPath(tray ?? Path.Combine(machine.MarqueeFolder, LogonTask.TrayExeName));
        if (!File.Exists(exe))
        {
            Console.Error.WriteLine(Strings.TrayNotFound(exe));
            return 1;
        }

        // The task starts the exe elevated at every logon, so whoever can change the file gets administrator rights. The
        // installer's copy is under Program Files, which only administrators can change, so the tray beside marquee.exe
        // must be too: anywhere else it is a copy or a build its user's own programs may be able to change. A --tray
        // elsewhere is a developer's choice, allowed but said out loud.
        if (!machine.AdminFolders.Any(folder => IsUnder(exe, folder)))
        {
            if (tray is null)
            {
                Console.Error.WriteLine(Strings.TrayNotInProgramFiles(exe));
                return 1;
            }

            Console.Error.WriteLine(Strings.TrayOutsideProgramFiles(exe));
        }

        try
        {
            LogonTask.Install(exe, machine.SchTasks, machine.MarqueeFolder);
            Console.WriteLine(Strings.TaskInstalled(LogonTask.TaskName, exe));
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            Console.Error.WriteLine(Strings.TaskInstallFailed(ex.Message));
            return 1;
        }

        SecureDataFolder(machine);
        return 0;
    }

    /// Makes %ProgramData%\Marquee, where an elevated tray keeps each user's log and downloads updates, and its updates
    /// folder, so that no other program can make them first: %ProgramData% lets every user make folders, and the tray
    /// refuses one someone else made. The installer runs this as SYSTEM right after copying the files, and on every
    /// upgrade and repair. SecureFolder makes each folder with its permissions set as it is made, and never follows,
    /// resets or uses one that is already there unless it is already safe, which Windows Installer's own folder
    /// creation could not promise. A folder it refuses is the user's to delete (the message says so); failing the
    /// install over it would only leave Marquee impossible to install, with no more than an error code to show, so it is
    /// a warning, and the tray, started elevated, says the same and stops until the folder is gone.
    static void SecureDataFolder(Machine machine)
    {
        try
        {
            machine.SecureFolder(machine.UpdatesFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(Strings.DataFolderNotSecured(ex.Message));
        }
    }

    /// With `updates`, the downloaded updates go too, whether or not the task could be removed: the uninstaller passes it.
    static int Remove(bool updates, Machine machine)
    {
        if (!machine.IsElevated())
        {
            Console.Error.WriteLine(Strings.TaskNeedsAdministrator("marquee task remove"));
            return 1;
        }

        var exit = 0;
        try
        {
            Console.WriteLine(LogonTask.Remove(machine.SchTasks)
                ? Strings.TaskRemoved(LogonTask.TaskName)
                : Strings.TaskWasNotInstalled(LogonTask.TaskName));
            RemoveTaskFolder(machine.Tasks);
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            Console.Error.WriteLine(Strings.TaskRemoveFailed(ex.Message));
            exit = 1;
        }

        if (updates) RemoveUpdates(machine.UpdatesFolder);
        return exit;
    }

    /// The task's folder stays in Task Scheduler's window after its last task goes, so it goes too once it is empty.
    /// One left behind is only clutter: a warning, which doesn't fail the uninstall.
    static void RemoveTaskFolder(ITaskRunner tasks)
    {
        try
        {
            tasks.DeleteFolderIfEmpty(LogonTask.TaskFolder);
        }
        catch (COMException ex)
        {
            Console.Error.WriteLine(Strings.TaskFolderNotRemoved(LogonTask.TaskFolder, ex.Message));
        }
    }

    /// Leftover installers are only clutter, so a failure here is a warning and doesn't fail the uninstall.
    static void RemoveUpdates(string folder)
    {
        try
        {
            var deleted = UpdateDownloads.Remove(folder);
            if (deleted > 0) Console.WriteLine(Strings.UpdatesRemoved(deleted, folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(Strings.UpdatesNotRemoved(ex.Message));
        }
    }

    /// Runs the task now for `userSid`, as the installer does when it finishes, so the tray starts elevated as at logon.
    static int Start(string userSid, Machine machine)
    {
        if (!machine.IsElevated())
        {
            Console.Error.WriteLine(Strings.TaskNeedsAdministrator("marquee task start"));
            return 1;
        }

        try
        {
            Console.WriteLine(LogonTask.Start(userSid, machine.Sessions, machine.Tasks).Count > 0
                ? Strings.TaskStarted(LogonTask.TaskName)
                : Strings.TaskStartNoSession(userSid));
            return 0;
        }
        catch (Exception ex) when (ex is LogonTaskException or Win32Exception or COMException)
        {
            Console.Error.WriteLine(Strings.TaskStartFailed(ex.Message));
            return 1;
        }
    }

    /// Closes every tray running from marquee.exe's folder, in every session, before the installer replaces or removes it.
    static int Stop(Machine machine)
    {
        if (!machine.IsElevated())
        {
            Console.Error.WriteLine(Strings.TaskNeedsAdministrator("marquee task stop"));
            return 1;
        }

        var tray = Path.Combine(machine.MarqueeFolder, LogonTask.TrayExeName);
        try
        {
            var result = TrayStopper.Stop(tray, machine.Trays, TrayStopper.Grace);
            Console.WriteLine(result.Quit + result.Killed > 0
                ? Strings.TraysStopped(tray, result.Quit, result.Killed)
                : Strings.NoTrayRunning(tray));
            return 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine(Strings.TrayStopFailed(ex.Message));
            return 1;
        }
    }

    static bool IsSid(string text)
    {
        try
        {
            _ = new SecurityIdentifier(text);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static int Status(Machine machine)
    {
        LogonTaskStatus status;
        try
        {
            status = LogonTask.Status(machine.SchTasks);
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            Console.Error.WriteLine(Strings.TaskStatusFailed(ex.Message));
            return 1;
        }

        if (!status.Installed)
        {
            Console.WriteLine(Strings.TaskNotInstalled(LogonTask.TaskName));
            return 1;
        }

        if (status.TrayExePath is not { } exe)
        {
            Console.WriteLine(Strings.TaskInstalledUnknownExe(LogonTask.TaskName));
            return 0;
        }

        Console.WriteLine(Strings.TaskInstalledStarts(LogonTask.TaskName, exe));
        if (!File.Exists(exe)) Console.Error.WriteLine(Strings.TaskTrayMissing(exe));
        return 0;
    }

    static bool IsUnder(string path, string folder) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// schtasks failing or not running, or the XML file not being written.
    static bool IsFailure(Exception ex) =>
        ex is LogonTaskException or Win32Exception or TimeoutException or IOException or UnauthorizedAccessException;
}
