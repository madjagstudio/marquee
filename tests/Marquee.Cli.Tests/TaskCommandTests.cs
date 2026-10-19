using System.ComponentModel;
using Marquee.Cli.Tests.TestSupport;
using Marquee.Core.Files;
using Marquee.Host;

namespace Marquee.Cli.Tests;

/// `marquee task install | remove | status`, with the elevation check, schtasks and marquee.exe's folder all faked: no
/// test here needs elevation or touches the real Task Scheduler. These capture the console, so they run in its collection.
[Collection(nameof(ConsoleCapture))]
public sealed class TaskCommandTests : IDisposable
{
    /// Stands in for marquee.exe's folder, where the installer puts MarqueeTray.exe.
    readonly string _folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "marquee-task-" + Guid.NewGuid().ToString("N")[..8])).FullName;

    static readonly string NotFound = $"ERROR: {new Win32Exception(2).Message}.";

    /// schtasks failing with `output` for the logon task, and saying "not found" for any other: it is asked about a task
    /// that can't exist to tell a missing task from other errors.
    static FakeSchTasks Failing(int exitCode, string output) => new(arguments => arguments.Contains(LogonTask.TaskName)
        ? new SchTasksResult(exitCode, output)
        : new SchTasksResult(1, NotFound));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Something still has a file open; %TEMP% is cleaned eventually.
        }
    }

    string Tray => Path.Combine(_folder, "MarqueeTray.exe");

    string AddTray()
    {
        File.WriteAllText(Tray, "");
        return Tray;
    }

    /// The user running the tests, as far as `task start` knows.
    const string Me = "S-1-5-21-1000-2000-3000-1001";

    FakeTaskRunner _runner = new();
    FakeSessions _sessions = new((Me, 1));
    FakeTrayProcesses _trays = new();

    /// Stands in for %ProgramData%\Marquee\updates.
    string Updates => Path.Combine(_folder, "ProgramData", "Marquee", "updates");

    /// The folders made admin-only, in place of SecureFolder.Ensure, which only works under the real %ProgramData%.
    readonly List<string> _secured = [];
    Exception? _secureError;

    /// `adminFolders` are the folders the install treats as admin-only (Program Files, outside tests).
    (int? Exit, string Out, string Error) Run(FakeSchTasks schtasks, bool elevated, string[] args, params string[] adminFolders)
    {
        using var console = new ConsoleCapture();
        var exit = TaskCommand.Run(args,
            new TaskCommand.Machine(() => elevated, schtasks, _folder, adminFolders, Me, _sessions, _runner, _trays, Updates, folder =>
            {
                if (_secureError is not null) throw _secureError;
                _secured.Add(folder);
            }));
        return (exit, console.Out.Trim(), console.Error.Trim());
    }

    [Theory]
    [InlineData("install")]
    [InlineData("remove")]
    [InlineData("start")]
    [InlineData("stop")]
    public void Changing_the_task_needs_an_administrator_terminal(string command)
    {
        AddTray();
        var schtasks = new FakeSchTasks();
        _trays = new FakeTrayProcesses(FakeTrayProcesses.Kind.QuitsWhenAsked);

        var (exit, _, error) = Run(schtasks, elevated: false, [command]);

        Assert.Equal(1, exit);
        Assert.StartsWith($"`marquee task {command}` needs an administrator terminal", error);
        Assert.Empty(schtasks.Calls);
        Assert.Empty(_runner.Runs);
        Assert.Null(_trays.AskedFor);
    }

    [Fact]
    public void Start_runs_the_task_in_the_users_session()
    {
        _sessions = new FakeSessions(("S-1-5-21-1000-2000-3000-1002", 1), (Me, 2));

        var (exit, output, error) = Run(new FakeSchTasks(), elevated: true, ["start"]);

        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Equal(@"Started the logon task \Marquee\Marquee: the tray is starting.", output);
        Assert.Equal([(@"\Marquee\Marquee", 2)], _runner.Runs);
    }

    /// The installer runs as SYSTEM and names the user who ran it.
    [Fact]
    public void Start_runs_the_task_for_the_user_it_is_given()
    {
        const string Other = "S-1-5-21-1000-2000-3000-1002";
        _sessions = new FakeSessions((Me, 1), (Other, 3));

        var (exit, _, _) = Run(new FakeSchTasks(), elevated: true, ["start", "--user", Other]);

        Assert.Equal(0, exit);
        Assert.Equal([(@"\Marquee\Marquee", 3)], _runner.Runs);
    }

    /// A silent install by a deployment tool, or on CI: no one to start the tray for, which is no error.
    [Fact]
    public void Start_for_a_user_who_is_not_signed_in_succeeds_and_says_when_the_tray_starts()
    {
        _sessions = new FakeSessions();

        var (exit, output, _) = Run(new FakeSchTasks(), elevated: true, ["start", "--user", "S-1-5-18"]);

        Assert.Equal(0, exit);
        Assert.Equal("S-1-5-18 is not signed in, so the tray was not started now. It starts at their next logon.", output);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void A_failed_start_says_why()
    {
        _runner = new FakeTaskRunner(new System.Runtime.InteropServices.COMException("The system cannot find the file specified."));

        var (exit, output, error) = Run(new FakeSchTasks(), elevated: true, ["start"]);

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Equal("Could not start the logon task: Task Scheduler could not start the task: The system cannot find the file specified.", error);
    }

    [Fact]
    public void Stop_closes_the_trays_running_from_marquee_exes_folder()
    {
        _trays = new FakeTrayProcesses(FakeTrayProcesses.Kind.QuitsWhenAsked, FakeTrayProcesses.Kind.Deaf);

        var (exit, output, error) = Run(new FakeSchTasks(), elevated: true, ["stop"]);

        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Equal(Tray, _trays.AskedFor);
        Assert.Equal($"Closed the trays running from {Tray}: 1 quit when asked, 1 had to be ended.", output);
    }

    [Fact]
    public void Stop_with_no_tray_running_says_so()
    {
        var (exit, output, _) = Run(new FakeSchTasks(), elevated: true, ["stop"]);

        Assert.Equal(0, exit);
        Assert.Equal($"No tray was running from {Tray}.", output);
    }

    [Fact]
    public void A_tray_that_cannot_be_ended_is_an_error()
    {
        _trays = new FakeTrayProcesses(FakeTrayProcesses.Kind.Unkillable);

        var (exit, _, error) = Run(new FakeSchTasks(), elevated: true, ["stop"]);

        Assert.Equal(1, exit);
        Assert.Equal($"Could not close the tray: {new Win32Exception(5).Message}", error);
    }

    [Fact]
    public void Remove_with_updates_also_deletes_the_downloaded_updates()
    {
        Directory.CreateDirectory(Updates);
        File.WriteAllText(Path.Combine(Updates, "Marquee-0.2.0-x64.msi"), "msi");
        File.WriteAllText(Path.Combine(Updates, "Marquee-0.2.0-x64.msi.sig"), "sig");
        var schtasks = new FakeSchTasks();

        var (exit, output, _) = Run(schtasks, elevated: true, ["remove", "--updates"]);

        Assert.Equal(0, exit);
        Assert.Equal(["/Delete", "/TN", @"\Marquee\Marquee", "/F"], Assert.Single(schtasks.Calls));
        Assert.EndsWith($"Deleted 2 downloaded update files and the folder {Updates}.", output);
        Assert.False(Directory.Exists(Updates));
    }

    [Fact]
    public void Remove_without_updates_leaves_them()
    {
        Directory.CreateDirectory(Updates);
        File.WriteAllText(Path.Combine(Updates, "Marquee-0.2.0-x64.msi"), "msi");

        Run(new FakeSchTasks(), elevated: true, ["remove"]);

        Assert.True(File.Exists(Path.Combine(Updates, "Marquee-0.2.0-x64.msi")));
    }

    /// Task Scheduler keeps a folder after its last task is deleted, so uninstalling would leave an empty \Marquee in its
    /// window.
    [Fact]
    public void Remove_also_deletes_the_tasks_folder_once_it_is_empty()
    {
        var (exit, output, error) = Run(new FakeSchTasks(), elevated: true, ["remove"]);

        Assert.Equal(0, exit);
        Assert.Equal(@"Removed the logon task \Marquee\Marquee.", output);
        Assert.Equal("", error);
        Assert.Equal([@"\Marquee"], _runner.DeletedFolders);
    }

    [Fact]
    public void Removing_a_task_that_is_not_there_still_deletes_its_empty_folder()
    {
        Run(new FakeSchTasks(1, NotFound), elevated: true, ["remove"]);

        Assert.Equal([@"\Marquee"], _runner.DeletedFolders);
    }

    /// A task deleted in Task Scheduler's window leaves its folder empty, and schtasks then says the path, not the
    /// file, can't be found.
    [Fact]
    public void Removing_a_task_deleted_by_hand_succeeds_and_deletes_its_empty_folder()
    {
        var (exit, output, error) = Run(Failing(1, $"ERROR: {new Win32Exception(3).Message}."), elevated: true, ["remove"]);

        Assert.Equal(0, exit);
        Assert.Equal(@"The logon task \Marquee\Marquee was not installed, so there was nothing to remove.", output);
        Assert.Equal("", error);
        Assert.Equal([@"\Marquee"], _runner.DeletedFolders);
    }

    [Fact]
    public void A_failed_remove_leaves_the_folder()
    {
        Run(Failing(1, "ERROR: Access is denied."), elevated: true, ["remove"]);

        Assert.Empty(_runner.DeletedFolders);
    }

    /// An empty folder left behind is only clutter, so it doesn't fail the uninstall.
    [Fact]
    public void A_folder_that_cannot_be_deleted_is_a_warning()
    {
        _runner = new FakeTaskRunner(folderError: new System.Runtime.InteropServices.COMException("Access is denied."));

        var (exit, output, error) = Run(new FakeSchTasks(), elevated: true, ["remove"]);

        Assert.Equal(0, exit);
        Assert.Equal(@"Removed the logon task \Marquee\Marquee.", output);
        Assert.Equal(@"Warning: could not delete the task folder \Marquee: Access is denied.", error);
    }

    /// The installer runs `task install` as SYSTEM, which makes %ProgramData%\Marquee and its updates folder admin-only
    /// before any other program can make them.
    [Fact]
    public void Install_makes_the_updates_folder_admin_only()
    {
        AddTray();

        var (exit, _, error) = Run(new FakeSchTasks(), elevated: true, ["install"], _folder);

        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Equal([Updates], _secured);
    }

    /// A folder someone else made is the user's to delete, and the tray says so itself: failing the install would only
    /// leave Marquee impossible to install.
    [Fact]
    public void A_data_folder_that_is_not_safe_is_a_warning_and_the_task_is_still_installed()
    {
        AddTray();
        _secureError = new UnsafePathException(@"Marquee won't keep files in C:\ProgramData\Marquee: it is owned by someone else.");
        var schtasks = new FakeSchTasks();

        var (exit, output, error) = Run(schtasks, elevated: true, ["install"], _folder);

        Assert.Equal(0, exit);
        Assert.StartsWith(@"Installed the logon task \Marquee\Marquee", output);
        Assert.StartsWith(@"Warning: Marquee won't keep files in C:\ProgramData\Marquee: it is owned by someone else. Marquee is installed", error);
        Assert.Single(schtasks.Calls);
    }

    [Fact]
    public void A_failed_install_makes_no_data_folder()
    {
        AddTray();

        Run(new FakeSchTasks(1, "ERROR: Access is denied."), elevated: true, ["install"], _folder);

        Assert.Empty(_secured);
    }

    [Fact]
    public void Install_starts_the_tray_next_to_marquee_exe_by_default()
    {
        AddTray();
        var schtasks = new FakeSchTasks();

        var (exit, output, error) = Run(schtasks, elevated: true, ["install"], _folder);

        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Equal($@"Installed the logon task \Marquee\Marquee: it starts {Tray} at every logon.", output);
        var call = Assert.Single(schtasks.Calls);
        Assert.Equal(["/Create", "/TN", @"\Marquee\Marquee", "/XML"], call.Take(4));
        // The XML file goes in marquee.exe's folder (admin-only once installed) and is gone afterwards.
        Assert.Equal(_folder, Path.GetDirectoryName(call[4]));
        Assert.Equal([Tray], Directory.GetFiles(_folder));
    }

    [Fact]
    public void Install_takes_another_tray_and_makes_its_path_full()
    {
        var other = Directory.CreateDirectory(Path.Combine(_folder, "build")).FullName;
        File.WriteAllText(Path.Combine(other, "Tray.exe"), "");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, Path.Combine(other, "Tray.exe"));
        string? xml = null;
        var schtasks = new FakeSchTasks(arguments =>
        {
            xml = File.ReadAllText(arguments[4]);
            return new SchTasksResult(0, "");
        });

        var (exit, output, _) = Run(schtasks, elevated: true, ["install", "--tray", relative], _folder);

        Assert.Equal(0, exit);
        Assert.Contains(Path.Combine(other, "Tray.exe"), output);
        Assert.Contains($"<Command>\"{Path.Combine(other, "Tray.exe")}\"</Command>", xml);
    }

    [Fact]
    public void Install_refuses_a_tray_that_does_not_exist()
    {
        var schtasks = new FakeSchTasks();

        var (exit, _, error) = Run(schtasks, elevated: true, ["install"], _folder);

        Assert.Equal(1, exit);
        Assert.StartsWith($"The tray app {Tray} does not exist.", error);
        Assert.Empty(schtasks.Calls);
    }

    /// The tray beside marquee.exe is the installer's, which is always under Program Files; one anywhere else is a
    /// marquee.exe copied or built somewhere its user's programs could change it, and the task would run it elevated.
    [Fact]
    public void Install_refuses_the_tray_beside_marquee_exe_outside_Program_Files()
    {
        AddTray();
        var schtasks = new FakeSchTasks();

        var (exit, output, error) = Run(schtasks, elevated: true, ["install"], @"C:\Program Files");

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.StartsWith($"{Tray} is not under Program Files, so the logon task was not installed.", error);
        Assert.Empty(schtasks.Calls);
    }

    /// A tray named with --tray is a developer's own choice: allowed, but said out loud.
    [Fact]
    public void Install_warns_about_a_tray_it_is_given_outside_Program_Files()
    {
        AddTray();
        var schtasks = new FakeSchTasks();

        var (exit, _, error) = Run(schtasks, elevated: true, ["install", "--tray", Tray], @"C:\Program Files");

        Assert.Equal(0, exit);
        Assert.StartsWith($"Warning: {Tray} is not under Program Files.", error);
        Assert.Single(schtasks.Calls);
    }

    [Fact]
    public void A_failed_install_says_why()
    {
        AddTray();

        var (exit, output, error) = Run(new FakeSchTasks(1, "ERROR: Access is denied."), elevated: true, ["install"], _folder);

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Equal("Could not install the logon task: schtasks exited with code 1: ERROR: Access is denied.", error);
    }

    [Fact]
    public void Remove_deletes_the_task()
    {
        var schtasks = new FakeSchTasks();

        var (exit, output, _) = Run(schtasks, elevated: true, ["remove"]);

        Assert.Equal(0, exit);
        Assert.Equal(@"Removed the logon task \Marquee\Marquee.", output);
        Assert.Equal(["/Delete", "/TN", @"\Marquee\Marquee", "/F"], Assert.Single(schtasks.Calls));
    }

    [Fact]
    public void Removing_a_task_that_is_not_there_succeeds_so_uninstall_does()
    {
        var (exit, output, _) = Run(new FakeSchTasks(1, NotFound), elevated: true, ["remove"]);

        Assert.Equal(0, exit);
        Assert.Equal(@"The logon task \Marquee\Marquee was not installed, so there was nothing to remove.", output);
    }

    [Fact]
    public void A_failed_remove_says_why()
    {
        var (exit, _, error) = Run(Failing(1, "ERROR: Access is denied."), elevated: true, ["remove"]);

        Assert.Equal(1, exit);
        Assert.Equal("Could not remove the logon task: schtasks exited with code 1: ERROR: Access is denied.", error);
    }

    [Fact]
    public void Status_names_the_exe_the_task_starts_and_needs_no_elevation()
    {
        var tray = AddTray();

        var (exit, output, error) = Run(new FakeSchTasks(0, LogonTask.Xml(tray)), elevated: false, ["status"]);

        Assert.Equal(0, exit);
        Assert.Equal($@"The logon task \Marquee\Marquee is installed; it starts {tray}.", output);
        Assert.Equal("", error);
    }

    [Fact]
    public void Status_warns_when_the_exe_the_task_starts_is_gone()
    {
        var (exit, _, error) = Run(new FakeSchTasks(0, LogonTask.Xml(Tray)), elevated: false, ["status"]);

        Assert.Equal(0, exit);
        Assert.StartsWith($"Warning: {Tray} does not exist", error);
    }

    [Fact]
    public void Status_of_a_task_that_is_not_there_exits_1()
    {
        var (exit, output, _) = Run(new FakeSchTasks(1, NotFound), elevated: false, ["status"]);

        Assert.Equal(1, exit);
        Assert.StartsWith(@"The logon task \Marquee\Marquee is not installed.", output);
    }

    [Fact]
    public void A_failed_status_says_why()
    {
        var (exit, _, error) = Run(Failing(5, "ERROR: Access is denied."), elevated: false, ["status"]);

        Assert.Equal(1, exit);
        Assert.Equal("Could not read the logon task: schtasks exited with code 5: ERROR: Access is denied.", error);
    }

    [Theory]
    [InlineData]
    [InlineData("add")]
    [InlineData("install", "--tray")]
    [InlineData("install", "--tray", " ")]
    [InlineData("install", "extra")]
    [InlineData("install", "--tray", "a.exe", "--tray", "b.exe")]
    [InlineData("remove", "--tray", "a.exe")]
    [InlineData("status", "now")]
    [InlineData("start", "--user")]
    [InlineData("start", "--user", "not-a-sid")]
    [InlineData("start", "now")]
    [InlineData("stop", "now")]
    [InlineData("remove", "--updates", "--updates")]
    public void Wrong_arguments_mean_usage(params string[] args)
    {
        var schtasks = new FakeSchTasks();

        var (exit, output, error) = Run(schtasks, elevated: true, args);

        Assert.Null(exit);
        Assert.Equal(("", ""), (output, error));
        Assert.Empty(schtasks.Calls);
    }

    [Fact]
    public void Usage_shows_the_command()
    {
        Assert.Contains("marquee task install [--tray <file>] | remove [--updates] | status | start [--user <SID>] | stop", Strings.Usage);
    }

    [Fact]
    public void Its_words_come_from_the_resources()
    {
        using var resources = SwappedResources.Pseudo();

        var (_, _, error) = Run(new FakeSchTasks(), elevated: false, ["remove"]);

        Assert.StartsWith("[!`marquee task remove` needs an administrator terminal", error);
        Assert.EndsWith("!]", error);
    }
}
