namespace Marquee.App.Tests;

/// What ExplorerShell hands explorer.exe, and that it opens nothing without the desktop's Explorer to hand it to. No
/// test here starts explorer.exe.
public class ExplorerShellTests
{
    [Theory]
    [InlineData(@"C:\Users\someone\AppData\Local\Marquee\themes", "\"C:\\Users\\someone\\AppData\\Local\\Marquee\\themes\"")]
    [InlineData(@"C:\Users\someone\AppData\Local\Marquee\themes\", "\"C:\\Users\\someone\\AppData\\Local\\Marquee\\themes\"")]
    [InlineData(@"C:\Marquee themes, old\config.yaml", "\"C:\\Marquee themes, old\\config.yaml\"")]
    [InlineData("https://github.com/owner/repo/releases/tag/v0.2.0", "\"https://github.com/owner/repo/releases/tag/v0.2.0\"")]
    public void The_path_is_always_quoted_and_never_ends_in_a_backslash(string path, string arguments)
    {
        Assert.Equal(arguments, ExplorerShell.Arguments(path));
    }

    /// With no desktop shell (Explorer crashed and hasn't restarted, or another shell replaced it), the explorer.exe
    /// the elevated tray starts may open the file itself, elevated. TrayModel shows the message in a balloon.
    [Fact]
    public void Without_the_desktops_Explorer_nothing_is_opened_and_the_reason_is_given()
    {
        var shell = new ExplorerShell(hasDesktopShell: () => false);

        var ex = Assert.Throws<InvalidOperationException>(() => shell.Open(@"C:\nowhere\config.yaml"));

        Assert.Contains("Explorer", ex.Message);
    }
}
