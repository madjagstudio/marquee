using System.ComponentModel;
using System.Text;
using System.Xml.Linq;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

/// The logon task's XML and the schtasks command lines that install, remove and query it, through a fake schtasks: no
/// test here creates, deletes or changes a real scheduled task.
public class LogonTaskTests
{
    static readonly XNamespace Task = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    const string Tray = @"C:\Program Files\Marquee\MarqueeTray.exe";

    /// What schtasks prints for a task that doesn't exist, in the PC's language: "ERROR: The system cannot find the file
    /// specified." on an English Windows.
    static readonly string NotFound = $"ERROR: {new Win32Exception(2).Message}.{Environment.NewLine}";

    /// The same on a French Windows, as the installer can get it: with no console, schtasks may write in a code page the
    /// output isn't decoded with, so its accented letters arrive garbled.
    const string GarbledNotFound = "ERREUR : le fichier sp�cifi� est introuvable.\r\n";

    /// schtasks answering `answer` about the logon task, and GarbledNotFound about any other: what it says for a task
    /// that can't exist.
    static FakeSchTasks Answering(int exitCode, string answer) => new(arguments => arguments.Contains(LogonTask.TaskName)
        ? new SchTasksResult(exitCode, answer)
        : new SchTasksResult(1, GarbledNotFound));

    static XElement Parse(string trayExePath) => XDocument.Parse(LogonTask.Xml(trayExePath)).Root!;

    static string Text(XElement element, params string[] path) =>
        path.Aggregate(element, (parent, name) => parent.Element(Task + name)
            ?? throw new Xunit.Sdk.XunitException($"No <{name}> under <{parent.Name.LocalName}>.")).Value;

    [Fact]
    public void The_task_has_its_own_folder()
    {
        Assert.Equal(@"\Marquee\Marquee", LogonTask.TaskName);
    }

    [Fact]
    public void The_xml_is_a_task_scheduler_1_2_task_declared_as_utf_16()
    {
        var document = XDocument.Parse(LogonTask.Xml(Tray));

        Assert.Equal("UTF-16", document.Declaration?.Encoding);
        Assert.Equal(Task + "Task", document.Root!.Name);
        Assert.Equal("1.2", document.Root.Attribute("version")?.Value);
    }

    [Fact]
    public void It_starts_at_the_logon_of_any_user_without_a_delay()
    {
        var trigger = Assert.Single(Parse(Tray).Element(Task + "Triggers")!.Elements());

        Assert.Equal(Task + "LogonTrigger", trigger.Name);
        Assert.Equal("true", Text(trigger, "Enabled"));
        Assert.Null(trigger.Element(Task + "UserId"));
        Assert.Null(trigger.Element(Task + "Delay"));
    }

    [Fact]
    public void It_runs_for_the_Users_group_with_highest_privileges()
    {
        var principal = Assert.Single(Parse(Tray).Element(Task + "Principals")!.Elements());

        Assert.Equal("S-1-5-32-545", Text(principal, "GroupId"));
        Assert.Equal("HighestAvailable", Text(principal, "RunLevel"));
        Assert.Null(principal.Element(Task + "UserId"));
        Assert.Equal(principal.Attribute("id")?.Value, Parse(Tray).Element(Task + "Actions")!.Attribute("Context")?.Value);
    }

    [Fact]
    public void Its_settings_keep_the_tray_running()
    {
        var settings = Parse(Tray).Element(Task + "Settings")!;

        Assert.Equal("PT0S", Text(settings, "ExecutionTimeLimit"));
        Assert.Equal("false", Text(settings, "DisallowStartIfOnBatteries"));
        Assert.Equal("false", Text(settings, "StopIfGoingOnBatteries"));
        Assert.Equal("true", Text(settings, "StartWhenAvailable"));
        Assert.Equal("true", Text(settings, "Enabled"));
        Assert.Equal("false", Text(settings, "Hidden"));
    }

    /// Task Scheduler counts a task's running instances across every user, and the tray's runs until logoff, so with
    /// IgnoreNew a second user signing in (fast user switching) would get no tray while the first one's runs.
    [Fact]
    public void Every_user_signing_in_gets_a_tray_of_their_own()
    {
        Assert.Equal("Parallel", Text(Parse(Tray), "Settings", "MultipleInstancesPolicy"));
    }

    [Fact]
    public void It_runs_at_normal_priority_not_the_task_default_of_below_normal()
    {
        Assert.Equal("4", Text(Parse(Tray), "Settings", "Priority"));
    }

    [Fact]
    public void It_starts_the_tray_quoted_in_its_own_folder()
    {
        var exec = Assert.Single(Parse(Tray).Element(Task + "Actions")!.Elements());

        Assert.Equal(Task + "Exec", exec.Name);
        Assert.Equal($"\"{Tray}\"", Text(exec, "Command"));
        Assert.Equal(@"C:\Program Files\Marquee", Text(exec, "WorkingDirectory"));
        Assert.Null(exec.Element(Task + "Arguments"));
    }

    [Fact]
    public void The_path_is_escaped_for_xml()
    {
        const string tray = @"C:\Tools\R&D <x>\MarqueeTray.exe";

        var xml = LogonTask.Xml(tray);

        Assert.Contains(@"R&amp;D &lt;x&gt;", xml);
        Assert.Equal($"\"{tray}\"", Text(Parse(tray), "Actions", "Exec", "Command"));
    }

    [Fact]
    public void It_describes_itself_for_Task_Schedulers_window()
    {
        Assert.Contains("Marquee", Text(Parse(Tray), "RegistrationInfo", "Description"));
    }

    [Fact]
    public void Install_creates_the_task_from_a_utf_16_file_and_deletes_the_file()
    {
        using var temp = new TempDir();
        byte[]? written = null;
        var schtasks = new FakeSchTasks(arguments =>
        {
            written = File.ReadAllBytes(arguments[4]);
            return new SchTasksResult(0, "SUCCESS: The scheduled task \"Marquee\" has successfully been created.");
        });

        LogonTask.Install(Tray, schtasks, temp.Path);

        var call = Assert.Single(schtasks.Calls);
        Assert.Equal(["/Create", "/TN", @"\Marquee\Marquee", "/XML", call[4], "/F"], call);
        Assert.Equal(temp.Path, Path.GetDirectoryName(call[4]));
        Assert.Equal([0xFF, 0xFE], written![..2]);
        Assert.Equal(LogonTask.Xml(Tray), Encoding.Unicode.GetString(written, 2, written.Length - 2));
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    /// schtasks reads the file by its path, so until it has, nothing may change it: the task runs elevated whatever the
    /// file says, and a `--tray` install may write it into a folder the user's own programs can change.
    [Fact]
    public void Install_holds_the_file_so_nothing_can_change_or_replace_it_until_schtasks_has_read_it()
    {
        using var temp = new TempDir();
        string? read = null;
        Exception? write = null;
        Exception? replace = null;
        var schtasks = new FakeSchTasks(arguments =>
        {
            var file = arguments[4];
            read = File.ReadAllText(file);
            write = Record.Exception(() => new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose());
            replace = Record.Exception(() => File.Move(file, file + ".old"));
            return new SchTasksResult(0, "");
        });

        LogonTask.Install(Tray, schtasks, temp.Path);

        Assert.Equal(LogonTask.Xml(Tray), read);
        Assert.IsType<IOException>(write);
        Assert.IsType<IOException>(replace);
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    [Fact]
    public void A_failed_install_says_what_schtasks_said_and_still_deletes_the_file()
    {
        using var temp = new TempDir();
        var schtasks = new FakeSchTasks(1, "ERROR: Access is denied.\r\n");

        var error = Assert.Throws<LogonTaskException>(() => LogonTask.Install(Tray, schtasks, temp.Path));

        Assert.Equal("schtasks exited with code 1: ERROR: Access is denied.", error.Message);
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    [Fact]
    public void Remove_deletes_the_task()
    {
        var schtasks = new FakeSchTasks(0, "SUCCESS: The scheduled task \"\\Marquee\\Marquee\" was successfully deleted.");

        Assert.True(LogonTask.Remove(schtasks));
        Assert.Equal(["/Delete", "/TN", @"\Marquee\Marquee", "/F"], Assert.Single(schtasks.Calls));
    }

    [Fact]
    public void Removing_a_task_that_is_not_there_succeeds()
    {
        Assert.False(LogonTask.Remove(new FakeSchTasks(1, NotFound)));
    }

    /// What schtasks says about a task that can't exist is what it says about a missing one, in whatever language and
    /// code page: so asking about one tells "not found" apart without reading the text.
    [Fact]
    public void A_missing_task_is_recognised_in_any_language_and_code_page()
    {
        var removing = Answering(1, GarbledNotFound);
        var querying = Answering(1, GarbledNotFound);

        Assert.False(LogonTask.Remove(removing));
        Assert.Equal(new LogonTaskStatus(Installed: false, TrayExePath: null), LogonTask.Status(querying));

        // The second call asks, read-only, about a task in a folder that can't exist.
        Assert.All([removing.Calls[1], querying.Calls[1]], probe =>
        {
            Assert.Equal(["/Query", "/TN"], probe.Take(2));
            Assert.Matches(@"^\\Marquee-missing-[0-9a-f]{32}\\Marquee$", probe[2]);
            Assert.Equal(3, probe.Count);
        });
    }

    [Fact]
    public void Another_error_is_not_taken_for_a_missing_task_in_any_language()
    {
        const string Denied = "ERREUR : acc�s refus�.";

        var error = Assert.Throws<LogonTaskException>(() => LogonTask.Remove(Answering(1, Denied)));
        Assert.Throws<LogonTaskException>(() => LogonTask.Status(Answering(1, Denied)));

        Assert.Equal($"schtasks exited with code 1: {Denied}", error.Message);
    }

    [Fact]
    public void Any_other_failure_to_remove_is_an_error()
    {
        var error = Assert.Throws<LogonTaskException>(() => LogonTask.Remove(Answering(1, "ERROR: Access is denied.")));

        Assert.Equal("schtasks exited with code 1: ERROR: Access is denied.", error.Message);
    }

    [Fact]
    public void Status_reads_the_installed_task_and_the_exe_it_starts()
    {
        // schtasks /Query /XML prints the registered task, with Task Scheduler's own additions such as the URI.
        var registered = LogonTask.Xml(Tray).Replace("<RegistrationInfo>", "<RegistrationInfo>\r\r\n    <URI>\\Marquee\\Marquee</URI>");
        var schtasks = new FakeSchTasks(0, registered);

        var status = LogonTask.Status(schtasks);

        Assert.Equal(new LogonTaskStatus(Installed: true, TrayExePath: Tray), status);
        Assert.Equal(["/Query", "/TN", @"\Marquee\Marquee", "/XML"], Assert.Single(schtasks.Calls));
    }

    [Fact]
    public void Status_of_a_task_that_is_not_there()
    {
        Assert.Equal(new LogonTaskStatus(Installed: false, TrayExePath: null), LogonTask.Status(new FakeSchTasks(1, NotFound)));
    }

    [Fact]
    public void Status_of_a_task_whose_xml_cannot_be_read_is_installed_without_a_path()
    {
        Assert.Equal(new LogonTaskStatus(Installed: true, TrayExePath: null), LogonTask.Status(new FakeSchTasks(0, "<Task")));
    }

    [Fact]
    public void Any_other_failure_to_query_is_an_error()
    {
        var error = Assert.Throws<LogonTaskException>(() => LogonTask.Status(Answering(5, "ERROR: Access is denied.")));

        Assert.Equal("schtasks exited with code 5: ERROR: Access is denied.", error.Message);
    }

    [Fact]
    public void The_real_schtasks_is_the_one_in_System32()
    {
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), SchTasks.ExePath);
    }

    /// Read-only: `/?` prints the help, without asking the Task Scheduler service anything.
    [Fact]
    public void The_real_runner_returns_what_schtasks_printed()
    {
        var result = new SchTasks().Run(["/?"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("/Create", result.Output);
    }

    /// Read-only: queries for tasks in folders that can't exist. It checks that "not found" is recognised from the real
    /// schtasks's stderr both ways: by its text in the PC's own language, and by asking about another missing task.
    [Fact]
    public void A_missing_task_is_recognised_in_the_real_schtasks_output()
    {
        var schtasks = new SchTasks();
        var result = schtasks.Run(["/Query", "/TN", $@"\Marquee-test-{Guid.NewGuid():N}\Missing"]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(LogonTask.IsNotFound(result.Output), result.Output);
        Assert.True(LogonTask.AnswersAsForAMissingTask(result, schtasks), result.Output);
    }
}
