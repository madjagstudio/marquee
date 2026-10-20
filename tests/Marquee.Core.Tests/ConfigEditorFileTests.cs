using Marquee.Core.Config;
using Marquee.Core.Files;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

public class ConfigEditorFileTests
{
    [Fact]
    public void Changes_the_one_key_in_the_file()
    {
        using var folder = new TempFolder();
        var path = folder.File(ConfigParser.FileName);
        File.WriteAllText(path, "# mine\r\ntheme: example-agents   # for now\r\nfps: 30\r\n");

        ConfigEditor.SetInFile(path, "theme", "neon");

        Assert.Equal("# mine\r\ntheme: neon   # for now\r\nfps: 30\r\n", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(folder.Path));
    }

    [Fact]
    public void Creates_a_missing_file_with_just_that_key()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "Marquee", ConfigParser.FileName);

        ConfigEditor.SetInFile(path, "theme", "neon");

        Assert.Equal("theme: neon\r\n", File.ReadAllText(path));
    }

    [Fact]
    public void Never_writes_over_a_broken_file()
    {
        using var folder = new TempFolder();
        var path = folder.File(ConfigParser.FileName);
        File.WriteAllText(path, "theme: [unclosed\n");

        Assert.Throws<ConfigException>(() => ConfigEditor.SetInFile(path, "theme", "neon"));

        Assert.Equal("theme: [unclosed\n", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(folder.Path));
    }

    /// Held open without delete sharing (as an editor might), the file can be read but not replaced, so trying to
    /// write it would fail.
    [Fact]
    public void Does_not_write_when_the_value_is_already_set()
    {
        using var folder = new TempFolder();
        var path = folder.File(ConfigParser.FileName);
        File.WriteAllText(path, "theme: neon\n");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            ConfigEditor.SetInFile(path, "theme", "neon");

        Assert.Equal("theme: neon\n", File.ReadAllText(path));
    }

    [Fact]
    public void A_failed_write_leaves_no_temporary_file()
    {
        using var folder = new TempFolder();
        var path = folder.File(ConfigParser.FileName);
        File.WriteAllText(path, "theme: old\n");

        // Held open without delete sharing, the file can be read but not replaced, so the move fails after the
        // temporary file is written.
        Exception? failure;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            failure = Record.Exception(() => ConfigEditor.SetInFile(path, "theme", "neon"));

        Assert.True(failure is IOException or UnauthorizedAccessException, $"Unexpected: {failure}");
        Assert.Equal([path], Directory.GetFileSystemEntries(folder.Path));
        Assert.Equal("theme: old\n", File.ReadAllText(path));
    }

    /// The tray runs elevated and config.yaml's folder is the user's: a read through a junction would hand the user an
    /// administrator's file, written back into their config.yaml with the one key changed. The file behind the
    /// junction here doesn't parse, so reading it would show as a ConfigException rather than the refusal.
    [Fact]
    public void Refuses_a_junction_on_the_path_before_reading_the_file_behind_it()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        File.WriteAllText(elsewhere.File(ConfigParser.FileName), "secret: [unclosed\n");
        var link = folder.File("Marquee");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            Assert.Throws<UnsafePathException>(() => ConfigEditor.SetInFile(Path.Combine(link, ConfigParser.FileName), "theme", "neon"));

            Assert.Equal("secret: [unclosed\n", File.ReadAllText(elsewhere.File(ConfigParser.FileName)));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void Refuses_a_symbolic_link_in_place_of_the_file_before_reading_its_target()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var target = elsewhere.File("secret.json");
        File.WriteAllText(target, "{ secret: [unclosed\n");
        var path = folder.File(ConfigParser.FileName);
        if (!Links.TryMakeFileSymbolicLink(path, target)) return;

        Assert.Throws<UnsafePathException>(() => ConfigEditor.SetInFile(path, "theme", "neon"));

        Assert.Equal("{ secret: [unclosed\n", File.ReadAllText(target));
    }
}
