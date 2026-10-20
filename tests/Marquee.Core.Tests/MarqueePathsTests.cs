using System.Security.Principal;
using Marquee.Core.Files;

namespace Marquee.Core.Tests;

public class MarqueePathsTests
{
    const string Sid = "S-1-5-21-1000-2000-3000-1001";
    static readonly MarqueeFolders Folders = MarqueePaths.For(@"C:\Users\someone\AppData\Local", @"C:\ProgramData", Sid);

    [Fact]
    public void Everything_lives_under_LocalAppData_Marquee()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Marquee");

        Assert.Equal(root, MarqueePaths.DataRoot);
        Assert.Equal(Path.Combine(root, "logs"), MarqueePaths.Logs);
        Assert.Equal(Path.Combine(root, "themes"), MarqueePaths.Themes);
        Assert.Equal(Path.Combine(root, "config.yaml"), MarqueePaths.ConfigFile);
    }

    [Fact]
    public void An_elevated_Marquee_keeps_its_data_in_a_folder_of_its_user_under_ProgramData()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        Assert.Equal(Path.Combine(programData, "Marquee", "users", identity.User!.Value), MarqueePaths.ElevatedDataRoot);
        Assert.Equal(MarqueePaths.ElevatedDataRoot, MarqueePaths.DataFolder(elevated: true));
        Assert.Equal(MarqueePaths.DataRoot, MarqueePaths.DataFolder(elevated: false));
        Assert.Equal(Path.Combine(MarqueePaths.ElevatedDataRoot, "logs"), MarqueePaths.LogsFolder(elevated: true));
        Assert.Equal(MarqueePaths.Logs, MarqueePaths.LogsFolder(elevated: false));
    }

    [Fact]
    public void The_folders_come_from_the_roots_given()
    {
        Assert.Equal(@"C:\Users\someone\AppData\Local\Marquee", Folders.DataRoot);
        Assert.Equal(@"C:\ProgramData\Marquee\users\" + Sid, Folders.ElevatedDataRoot);
        Assert.Equal(Folders.DataRoot, Folders.DataFolder(elevated: false));
        Assert.Equal(Folders.ElevatedDataRoot, Folders.DataFolder(elevated: true));
        Assert.Equal(@"C:\Users\someone\AppData\Local\Marquee\logs", Folders.LogsFolder(elevated: false));
        Assert.Equal(@"C:\ProgramData\Marquee\users\" + Sid + @"\logs", Folders.LogsFolder(elevated: true));
    }

    [Fact]
    public void Config_and_themes_stay_where_the_user_edits_them_even_for_an_elevated_Marquee()
    {
        Assert.Equal(@"C:\Users\someone\AppData\Local\Marquee\config.yaml", Folders.ConfigFile);
        Assert.Equal(@"C:\Users\someone\AppData\Local\Marquee\themes", Folders.Themes);
    }

    [Fact]
    public void Downloaded_updates_go_to_one_folder_for_the_machine_under_ProgramData()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        Assert.Equal(@"C:\ProgramData\Marquee\updates", Folders.Updates);
        Assert.Equal(Path.Combine(programData, "Marquee", "updates"), MarqueePaths.Updates);
    }

    [Fact]
    public void An_unelevated_process_keeps_its_data_in_LocalAppData_and_secures_nothing()
    {
        var secured = new List<string>();

        Assert.Equal(Folders.DataRoot, MarqueePaths.PrepareDataFolder(Folders, elevated: false, secured.Add));
        Assert.Empty(secured);
    }

    [Fact]
    public void An_elevated_process_makes_its_ProgramData_folder_admin_only_before_using_it()
    {
        var secured = new List<string>();

        Assert.Equal(Folders.ElevatedDataRoot, MarqueePaths.PrepareDataFolder(Folders, elevated: true, secured.Add));
        Assert.Equal([Folders.ElevatedDataRoot], secured);
    }

    [Fact]
    public void An_elevated_process_never_falls_back_to_LocalAppData_when_its_folder_cant_be_secured()
    {
        var ex = Assert.Throws<UnsafePathException>(() =>
            MarqueePaths.PrepareDataFolder(Folders, elevated: true, _ => throw new UnsafePathException("not safe")));

        Assert.Equal("not safe", ex.Message);
    }
}
