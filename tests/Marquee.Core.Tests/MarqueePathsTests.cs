namespace Marquee.Core.Tests;

public class MarqueePathsTests
{
    [Fact]
    public void Everything_lives_under_LocalAppData_Marquee()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Marquee");

        Assert.Equal(root, MarqueePaths.DataRoot);
        Assert.Equal(Path.Combine(root, "logs"), MarqueePaths.Logs);
        Assert.Equal(Path.Combine(root, "themes"), MarqueePaths.Themes);
        Assert.Equal(Path.Combine(root, "config.yaml"), MarqueePaths.ConfigFile);
    }
}
