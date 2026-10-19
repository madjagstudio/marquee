using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

public class ConfigFileTests
{
    [Fact]
    public void A_missing_file_is_the_defaults_without_a_word()
    {
        using var dir = new TempDir();
        var log = new ListLog();

        Assert.Equal(MarqueeConfig.Default, ConfigFile.Load(dir.File("config.yaml"), log));
        Assert.Equal(MarqueeConfig.Default, ConfigFile.Load(dir.File(@"not-yet\config.yaml"), log));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void A_good_file_is_parsed()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.yaml", "theme: neon\norientation: portrait\nfps: 20\n");
        var log = new ListLog();

        Assert.Equal(new MarqueeConfig { Theme = "neon", Orientation = Orientation.Portrait, Fps = 20 }, ConfigFile.Load(path, log));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void A_bad_file_is_the_defaults_with_the_error_and_its_line_logged()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.yaml", "theme: neon\nfps: 0\n");
        var log = new ListLog();

        Assert.Equal(MarqueeConfig.Default, ConfigFile.Load(path, log));
        Assert.Equal(["error: config.yaml line 2: fps must be between 1 and 60 (Marquee uses the default settings until the file is fixed)"],
            log.Entries);
    }

    [Fact]
    public void An_unreadable_file_is_the_defaults_with_the_reason_logged()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.yaml", "theme: neon\n");
        var log = new ListLog();

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(MarqueeConfig.Default, ConfigFile.Load(path, log));

        var entry = Assert.Single(log.Entries);
        Assert.StartsWith($"error: Could not read {path} (", entry);
        Assert.EndsWith("); using the default settings.", entry);
    }

    [Fact]
    public void Read_throws_for_a_bad_file_so_a_watcher_can_keep_the_last_good_one()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.yaml", "sensors: aida64\n");

        Assert.Equal(1, Assert.Throws<ConfigException>(() => ConfigFile.Read(path)).Line);
    }
}
