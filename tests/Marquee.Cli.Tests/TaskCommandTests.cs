using System.ComponentModel;
using Marquee.Cli.Tests.TestSupport;
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

    /// `adminFolders` are the folders the install treats as admin-only (Program Files, outside tests).
    (int? Exit, string Out, string Error) Run(FakeSchTasks schtasks, bool elevated, string[] args, params string[] adminFolders)
    {
        using var console = new ConsoleCapture();
        var exit = TaskCommand.Run(args, new TaskCommand.Machine(() => elevated, schtasks, _folder, adminFolders));
        return (exit, console.Out.Trim(), console.Error.Trim());
    }

    [Theory]
    [InlineData("install")]
    [InlineData("remove")]
    public void Changing_the_task_needs_an_administrator_terminal(string command)
    {
        AddTray();
        var schtasks = new FakeSchTasks();

        var (exit, _, error) = Run(schtasks, elevated: false, [command]);

        Assert.Equal(1, exit);
        Assert.StartsWith($"`marquee task {command}` needs an administrator terminal", error);
        Assert.Empty(schtasks.Calls);
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
        Assert.Contains("marquee task install [--tray <file>] | remove | status", Strings.Usage);
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
