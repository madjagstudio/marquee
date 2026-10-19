using System.ComponentModel;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Marquee.Host.Resources;

namespace Marquee.Host;

/// The scheduled task that starts the tray at every user's logon, elevated where the user can be: "run with highest
/// privileges" for the Users group. For a standard user "highest" is their normal token, so the tray still starts, with
/// FPS and the L-Connect handover off. The installer runs `marquee task install` and `remove` (elevated); creating a
/// task that runs elevated needs an administrator, which is what keeps an unelevated program from making one.
public static class LogonTask
{
    /// In a folder of its own, so Task Scheduler's window shows it apart from Windows' tasks.
    public const string TaskName = @"\Marquee\Marquee";

    /// The tray app, installed next to marquee.exe.
    public const string TrayExeName = "MarqueeTray.exe";

    /// BUILTIN\Users by its well-known SID, which is the same in every language (the group's name is translated).
    const string UsersGroup = "S-1-5-32-545";

    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// The task in Task Scheduler's XML (schema 1.2, Windows 7 and later), for `schtasks /Create /XML`.
    public static string Xml(string trayExePath)
    {
        var task = new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Author", "Marquee"),
                new XElement(Ns + "Description", Strings.LogonTaskDescription)),
            // Any user's logon, with no delay: at logon the screen may not be enumerated yet and Explorer's notification
            // area may not exist, but the engine retries the screen and the tray re-adds its icon when the taskbar
            // appears (TaskbarCreated), so waiting would only leave the screen blank for longer.
            new XElement(Ns + "Triggers",
                new XElement(Ns + "LogonTrigger",
                    new XElement(Ns + "Enabled", "true"))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal", new XAttribute("id", "Users"),
                    new XElement(Ns + "GroupId", UsersGroup),
                    new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(Ns + "Settings",
                // A tray for every user who signs in. Task Scheduler counts a task's instances across all users, and the
                // tray's runs until logoff, so IgnoreNew would leave a second user (fast user switching) without one. A
                // second copy for the same user finds the first one's per-user instance mutex, opens its menu and exits.
                new XElement(Ns + "MultipleInstancesPolicy", "Parallel"),
                // A laptop on battery still shows the screen; the defaults would not start it, or stop it on unplugging.
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "StartWhenAvailable", "true"),
                new XElement(Ns + "AllowStartOnDemand", "true"),
                new XElement(Ns + "Enabled", "true"),
                // Visible in Task Scheduler's window, so a user can find it, run it or turn it off there.
                new XElement(Ns + "Hidden", "false"),
                // The tray runs until logoff; the default limit of 72 hours would stop it after three days.
                new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                // A task's default priority, 7, starts it below normal (CPU, I/O and memory priority), which would slow
                // rendering whenever anything else is busy, a game most of all. 4 is normal, like an app started by hand.
                new XElement(Ns + "Priority", "4")),
            new XElement(Ns + "Actions", new XAttribute("Context", "Users"),
                new XElement(Ns + "Exec",
                    // Quoted, because Program Files has a space in it.
                    new XElement(Ns + "Command", $"\"{trayExePath}\""),
                    new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(trayExePath)))));

        var document = new XDocument(new XDeclaration("1.0", "UTF-16", null), task);
        return document.Declaration + Environment.NewLine + document;
    }

    /// Creates the task, replacing one of the same name (an upgrade). schtasks reads the XML from a file, written as
    /// UTF-16 with a byte order mark as its declaration says, in `tempFolder` and deleted afterwards. The task runs
    /// elevated whatever the file says, so `tempFolder` should be one only administrators can write (marquee.exe's own
    /// folder under Program Files). Wherever it is, the file is held open until schtasks has read it, shared for reading
    /// only, and what it holds is checked through that handle first: from then on nothing can change, delete or rename
    /// it, so schtasks reads exactly what was written.
    public static void Install(string trayExePath, ISchTasks schtasks, string tempFolder)
    {
        var file = Path.Combine(tempFolder, $"marquee-task-{Guid.NewGuid():N}.xml");
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(Xml(trayExePath))];
        try
        {
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes);

            // A handle that only reads, so schtasks can open the file too; one that could write would stop it unless it
            // shared writing. Something could have replaced the file between closing it and this, so it is read back.
            using (var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var read = new byte[bytes.Length + 1];
                if (held.ReadAtLeast(read, read.Length, throwOnEndOfStream: false) != bytes.Length || !read.AsSpan(0, bytes.Length).SequenceEqual(bytes))
                    throw new IOException(Strings.TaskFileChanged(file));

                Check(schtasks.Run(["/Create", "/TN", TaskName, "/XML", file, "/F"]));
            }
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // It holds nothing secret, and the next install writes a file of its own.
            }
        }
    }

    /// Deletes the task. False means there was no task to delete, which uninstalling counts as success.
    public static bool Remove(ISchTasks schtasks)
    {
        var result = schtasks.Run(["/Delete", "/TN", TaskName, "/F"]);
        if (IsMissing(result, schtasks)) return false;
        Check(result);
        return true;
    }

    /// Whether the task exists, and the exe it starts when its XML says.
    public static LogonTaskStatus Status(ISchTasks schtasks)
    {
        var result = schtasks.Run(["/Query", "/TN", TaskName, "/XML"]);
        if (IsMissing(result, schtasks)) return new LogonTaskStatus(Installed: false, TrayExePath: null);
        Check(result);
        return new LogonTaskStatus(Installed: true, TrayExePath: Command(result.Output));
    }

    /// Whether schtasks failed because the task, or its folder, isn't there.
    static bool IsMissing(SchTasksResult result, ISchTasks schtasks) =>
        result.ExitCode != 0 && (IsNotFound(result.Output) || AnswersAsForAMissingTask(result, schtasks));

    /// schtasks says a task or its folder is missing with Windows' own text for ERROR_FILE_NOT_FOUND ("The system
    /// cannot find the file specified."), in the PC's language, which is the text Windows gives .NET for that code.
    internal static bool IsNotFound(string output) =>
        output.Contains(new Win32Exception(2).Message, StringComparison.OrdinalIgnoreCase);

    /// The same without reading the text, which can arrive garbled: schtasks writes it in a code page that depends on
    /// its console, and with none (the installer runs marquee.exe without one) that need not be the one its output is
    /// decoded with, so a translated message's accented letters no longer match. schtasks is asked, read-only, about a
    /// task in a folder that can't exist; if it answers exactly as it did, the task is missing too.
    internal static bool AnswersAsForAMissingTask(SchTasksResult result, ISchTasks schtasks)
    {
        var missing = schtasks.Run(["/Query", "/TN", $@"\Marquee-missing-{Guid.NewGuid():N}\Marquee"]);
        return missing.ExitCode == result.ExitCode && missing.Output.Trim() == result.Output.Trim();
    }

    static string? Command(string xml)
    {
        try
        {
            var command = XDocument.Parse(xml.Trim()).Root?.Element(Ns + "Actions")?.Element(Ns + "Exec")?.Element(Ns + "Command");
            return command is null ? null : command.Value.Trim().Trim('"');
        }
        catch (XmlException)
        {
            return null;
        }
    }

    static void Check(SchTasksResult result)
    {
        if (result.ExitCode != 0) throw new LogonTaskException(Strings.SchTasksFailed(result.ExitCode, result.Output.Trim()));
    }
}

/// Whether the logon task exists, and the exe it starts (null when that couldn't be read).
public sealed record LogonTaskStatus(bool Installed, string? TrayExePath);

/// schtasks failed; the message has its exit code and what it printed.
public sealed class LogonTaskException(string message) : Exception(message);
