using System.ComponentModel;
using Marquee.Core.Processes;
using Marquee.Host;

/// `marquee task install [--tray <file>] | remove | status`: the elevated logon task that starts the tray. The installer
/// runs install and remove; a user can run them from an administrator terminal. Status needs no elevation, and exits 0
/// when the task is installed and 1 when it isn't, so a script can test for it.
static class TaskCommand
{
    /// What the command needs from the PC, so tests can fake it: whether Marquee runs elevated, schtasks, marquee.exe's
    /// folder (where the installer puts the tray, and where the task's XML is written) and the admin-only folders a tray
    /// should be under.
    internal sealed record Machine(Func<bool> IsElevated, ISchTasks SchTasks, string MarqueeFolder, IReadOnlyList<string> AdminFolders)
    {
        public static Machine Real => new(ProcessElevation.IsCurrentProcessElevated, new SchTasks(), AppContext.BaseDirectory,
        [
            .. new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .Select(Environment.GetFolderPath)
                .Where(folder => folder.Length > 0),
        ]);
    }

    /// Null means the arguments were wrong and the caller should print usage.
    public static int? Run(string[] args) => Run(args, Machine.Real);

    internal static int? Run(string[] args, Machine machine) => args switch
    {
        ["install"] => Install(null, machine),
        ["install", "--tray", var tray] when tray.Trim().Length > 0 => Install(tray, machine),
        ["remove"] => Remove(machine),
        ["status"] => Status(machine),
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
            return 0;
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            Console.Error.WriteLine(Strings.TaskInstallFailed(ex.Message));
            return 1;
        }
    }

    static int Remove(Machine machine)
    {
        if (!machine.IsElevated())
        {
            Console.Error.WriteLine(Strings.TaskNeedsAdministrator("marquee task remove"));
            return 1;
        }

        try
        {
            Console.WriteLine(LogonTask.Remove(machine.SchTasks)
                ? Strings.TaskRemoved(LogonTask.TaskName)
                : Strings.TaskWasNotInstalled(LogonTask.TaskName));
            return 0;
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            Console.Error.WriteLine(Strings.TaskRemoveFailed(ex.Message));
            return 1;
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
