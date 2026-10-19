using Marquee.Cli.Tests.TestSupport;

namespace Marquee.Cli.Tests;

public class HardCodedMessageTests
{
    // Console writes that start with a literal but print data, not words: nothing in them to translate. Each entry is
    // the file name and the exact trimmed line, so editing the line brings it back here for a second look.
    static readonly (string File, string Line)[] Allowed =
    [
        // `marquee devices`: one row per screen, vendor:product ID and the device path.
        ("Program.cs", "Console.WriteLine($\"{entry.VendorId:X4}:{entry.ProductId:X4}  {entry.Path}\");"),
        // `marquee sensors`: one row per sensor, the key and its value.
        ("SensorsCommand.cs", "Console.WriteLine($\"{key,-28} {Formatting.Format(value)}\");"),
    ];

    static List<(string File, int Line, string Text)> CliHits()
    {
        var cli = SourceTree.Path("src", "Marquee.Cli");
        return Directory.EnumerateFiles(cli, "*.cs", SearchOption.AllDirectories)
            .Where(file => Path.GetRelativePath(cli, file).Split(Path.DirectorySeparatorChar)[0] is not ("bin" or "obj"))
            .SelectMany(file => HardCodedMessages.Find(File.ReadAllText(file)).Select(hit => (file, hit.Line, hit.Text)))
            .ToList();
    }

    static bool IsAllowed((string File, int Line, string Text) hit) =>
        Allowed.Contains((Path.GetFileName(hit.File), hit.Text));

    [Fact]
    public void No_CLI_source_writes_a_hard_coded_message()
    {
        var offenders = CliHits()
            .Where(hit => !IsAllowed(hit))
            .Select(hit => $"{Path.GetRelativePath(SourceTree.Root, hit.File)}:{hit.Line}: {hit.Text}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Put these messages in src/Marquee.Cli/Resources/Strings.resx and write them with Strings:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Every_allowed_line_still_exists()
    {
        var hits = CliHits();

        foreach (var allowed in Allowed)
            Assert.Contains(hits, hit => Path.GetFileName(hit.File) == allowed.File && hit.Text == allowed.Line);
    }

    [Theory]
    [InlineData("Console.WriteLine(\"No supported screens found.\");")]
    [InlineData("Console.Error.WriteLine($\"Could not read image: {file}\");")]
    [InlineData("Console.Out.WriteLine(@\"Done.\");")]
    [InlineData("Console.Write($@\"Sent {file}.\");")]
    [InlineData("Console.Error.Write(\"Warning: \");")]
    [InlineData("writer.Error.WriteLine(\"Failed.\");")]
    [InlineData("Console.Error.WriteLine(\"\"\"\n    Usage:\n      marquee devices\n    \"\"\");")]
    [InlineData("Console.WriteLine($\"\"\"Sent {file}.\"\"\");")]
    public void The_scanner_flags_a_message_written_as_a_literal(string source)
    {
        Assert.Single(HardCodedMessages.Find(source));
    }

    [Fact]
    public void The_scanner_reports_the_line_of_the_call_when_the_literal_is_on_the_next_one()
    {
        var source = "var size = 1;\r\nif (wrong)\r\n    Console.Error.WriteLine(\r\n        $\"Warning: image is {size}.\");\r\n";

        var hit = Assert.Single(HardCodedMessages.Find(source));

        Assert.Equal(3, hit.Line);
        Assert.Equal("Console.Error.WriteLine(", hit.Text);
    }

    [Theory]
    [InlineData("Console.WriteLine(Strings.NoSupportedScreens);")]
    [InlineData("Console.Error.WriteLine(Strings.CouldNotReadImage(file));")]
    [InlineData("Console.Error.WriteLine(ex.Message);")]
    [InlineData("Console.WriteLine(text);")]
    [InlineData("Console.WriteLine();")]
    [InlineData("var path = \"Console.txt\"; Console.Out.Flush();")]
    public void The_scanner_passes_a_message_from_the_resources(string source)
    {
        Assert.Empty(HardCodedMessages.Find(source));
    }
}
