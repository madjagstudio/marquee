using System.IO.Compression;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Validation;
using static Marquee.Core.Tests.TestSupport.Validations;

namespace Marquee.Core.Tests;

/// `marquee validate theme.zip`: the theme is checked as a folder once extracted, and the zip itself must be safe to
/// extract (no entry outside the folder, no link, no bomb, within the limits) before anything is written.
public class ThemeZipTests : IDisposable
{
    const string Layout = "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#203040\"\n";

    readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    string Zip(params (string Name, object? Contents)[] entries) => ZipNamed("theme.zip", entries);

    string ZipNamed(string name, params (string Name, object? Contents)[] entries)
    {
        var path = _folder.File(name);
        Zips.Write(path, entries);
        return path;
    }

    [Fact]
    public void A_theme_at_the_top_of_the_zip_is_named_after_the_zip()
    {
        var report = Validate(ZipNamed("neon.zip", ("layout.yaml", Layout), ("shaders/", null), ("shaders/s.sksl", "half4 main(float2 p) { return half4(1); }")));

        Assert.Empty(report.Issues);
        Assert.Equal("neon", report.Name);
        Assert.Equal(new CanvasSpec(ThemeOrientation.Landscape, 30), report.Canvas);
    }

    [Fact]
    public void A_theme_in_one_top_level_folder_is_named_after_the_folder()
    {
        var report = Validate(ZipNamed("download.zip", ("neon/", null), ("neon/layout.yaml", Layout), ("neon/notes.txt", "hi")));

        Assert.Empty(report.Issues);
        Assert.Equal("neon", report.Name);
    }

    [Fact]
    public void Problems_in_a_zipped_theme_name_files_inside_the_theme()
    {
        var report = Validate(Zip(("neon/layout.yaml", Layout + "  - image: images/gone.png\n  - fill: nope\n")));

        Assert.Equal(
            [
                "images/gone.png:0: error: Image 'images/gone.png' was not found and was skipped.",
                "layout.yaml:5: error: fill: 'nope' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"",
            ],
            Lines(report));
    }

    [Fact]
    public void Macs_metadata_folder_is_ignored()
    {
        var report = Validate(Zip(("neon/layout.yaml", Layout), ("__MACOSX/neon/._layout.yaml", "resource fork")));

        Assert.Empty(report.Issues);
        Assert.Equal("neon", report.Name);
    }

    [Fact]
    public void Two_top_level_folders_is_an_error_saying_where_layout_yaml_belongs()
    {
        var report = Validate(Zip(("neon/layout.yaml", Layout), ("extras/readme.txt", "hi")));

        Assert.Equal(
            [":0: error: There is no layout.yaml at the top of the zip, and its files aren't all in one folder. " +
                "Put layout.yaml at the top level, or the whole theme in one folder."],
            Lines(report));
        Assert.Equal("theme", report.Name);
        Assert.Null(report.Canvas);
    }

    [Fact]
    public void A_folder_without_layout_yaml_is_an_error_saying_where_it_belongs()
    {
        var report = Validate(Zip(("neon/sub/layout.yaml", Layout)));

        Assert.Equal([":0: error: There is no layout.yaml in the zip's folder 'neon'; it should be at neon/layout.yaml."], Lines(report));
    }

    [Fact]
    public void An_empty_zip_is_an_error()
    {
        Assert.Equal([":0: error: The zip is empty. layout.yaml should be at its top level or in its one top-level folder."],
            Lines(Validate(Zip())));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("neon/../../evil.txt")]
    [InlineData("..\\evil.txt")]
    public void An_entry_reaching_outside_the_zip_is_refused(string name)
    {
        var report = Validate(Zip(("layout.yaml", Layout), (name, "x")));

        Assert.Equal([$":0: error: Entry '{name}' uses '..' to reach outside the zip."], Lines(report));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "evil.txt")));
    }

    [Theory]
    [InlineData("/abs.txt")]
    [InlineData("\\abs.txt")]
    [InlineData("C:/abs.txt")]
    [InlineData("C:abs.txt")]
    public void An_entry_with_an_absolute_path_is_refused(string name)
    {
        Assert.Equal([$":0: error: Entry '{name}' has an absolute path; every file must be inside the theme."],
            Lines(Validate(Zip(("layout.yaml", Layout), (name, "x")))));
    }

    [Theory]
    [InlineData("images/a:b.txt")]
    [InlineData("what?.txt")]
    [InlineData("images/nul.png")]
    [InlineData("CON")]
    [InlineData("trailing./x.txt")]
    public void An_entry_windows_cannot_name_is_refused(string name)
    {
        Assert.Equal([$":0: error: Entry '{name}' is not a valid file name on Windows."],
            Lines(Validate(Zip(("layout.yaml", Layout), (name, "x")))));
    }

    [Fact]
    public void A_symbolic_link_entry_is_refused()
    {
        var path = _folder.File("theme.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var layout = new StreamWriter(archive.CreateEntry("layout.yaml").Open())) layout.Write(Layout);
            var link = archive.CreateEntry("secrets");
            link.ExternalAttributes = unchecked((int)0xA1FF_0000); // a Unix zip's mode bits: S_IFLNK, rwxrwxrwx
            using var target = new StreamWriter(link.Open());
            target.Write("/etc/passwd");
        }

        Assert.Equal([":0: error: Entry 'secrets' is a symbolic link, which a shared theme can't contain."], Lines(Validate(path)));
    }

    [Fact]
    public void A_zip_with_more_entries_than_a_theme_may_have_files_is_refused()
    {
        var path = Zip(("layout.yaml", Layout), ("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));

        Assert.Equal([":0: error: The zip has 4 entries, more than the 3 files a theme may have."],
            Lines(Validate(path, ThemeLimits.Default with { MaxFiles = 3 })));
        Assert.Empty(Validate(path, ThemeLimits.Default with { MaxFiles = 4 }).Issues);
    }

    [Fact]
    public void A_zip_whose_directory_holds_more_entries_than_it_declares_is_refused_at_the_limit()
    {
        var path = Zip(("layout.yaml", Layout), ("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));
        Zips.UnderstateCount(path, 2);

        Assert.Equal([":0: error: The zip's directory lists more than 3 entries, though the zip says it has 2; a theme may have at most 3 files."],
            Lines(Validate(path, ThemeLimits.Default with { MaxFiles = 3 })));
    }

    [Fact]
    public void A_zip_listing_many_entries_is_refused_without_reading_them_all()
    {
        // A zip of about 100 MB can list over a million entries. Reading them all, an object each, before counting them
        // would cost hundreds of megabytes; here, 20,000 entries would cost several.
        var path = _folder.File("theme.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            for (var i = 0; i < 20_000; i++) archive.CreateEntry($"f{i}.txt");
        Zips.UnderstateCount(path, 2); // and past a check of the count the zip declares
        var issues = new List<ThemeIssue>();

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using (ThemeZip.Extract(path, ThemeLimits.Default with { MaxFiles = 10 }, issues)) { }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        Assert.StartsWith("The zip's directory lists more than 10 entries", Assert.Single(issues).Message);
        Assert.True(allocated < 1024 * 1024, $"allocated {allocated:N0} bytes");
    }

    [Fact]
    public void A_zip_file_larger_than_a_theme_may_be_is_refused_before_it_is_opened()
    {
        // Not even a zip: the size alone refuses it, before anything reads its directory.
        var path = _folder.File("theme.zip");
        File.WriteAllBytes(path, Zips.Noise(1100 * 1024));

        Assert.Equal([":0: error: The zip file is 2 MB, over the 1 MB limit for a theme."],
            Lines(Validate(path, ThemeLimits.Default with { MaxTotalBytes = 1024 * 1024 })));
    }

    [Fact]
    public void A_zip_over_the_total_size_limit_is_refused_before_extracting()
    {
        // The zip is under the limit; what it would expand to isn't.
        var path = Zip(("layout.yaml", Layout), ("a.bin", Zips.HalfNoise(600 * 1024)), ("b.bin", Zips.HalfNoise(600 * 1024)));
        Assert.True(new FileInfo(path).Length < 1024 * 1024);

        Assert.Equal([":0: error: The zip's files add up to 2 MB uncompressed, over the 1 MB limit for a theme."],
            Lines(Validate(path, ThemeLimits.Default with { MaxTotalBytes = 1024 * 1024 })));
    }

    [Fact]
    public void An_entry_that_expands_too_much_is_refused_as_a_possible_zip_bomb()
    {
        // A megabyte of zeros deflates to about a kilobyte: a thousand to one. Real theme files don't come close to 100.
        Assert.Equal(100, ThemeLimits.Default.MaxCompressionRatio);
        var report = Validate(Zip(("layout.yaml", Layout), ("zeros.bin", new byte[1024 * 1024])));

        var issue = Assert.Single(report.Issues);
        Assert.Equal("", issue.File);
        Assert.StartsWith("Entry 'zeros.bin' would expand more than 100 times, from ", issue.Message);
        Assert.EndsWith(" bytes to 1,048,576; the zip is refused as a possible zip bomb.", issue.Message);
    }

    [Fact]
    public void An_entry_larger_than_the_zip_says_is_refused()
    {
        var path = Zip(("big.bin", Zips.Noise(100_000)), ("layout.yaml", Layout));
        Zips.Understate(path, 1000);
        using (var archive = ZipFile.OpenRead(path))
            Assert.Equal(1000, archive.Entries[0].Length); // what a size check would see

        Assert.Equal([":0: error: Entry 'big.bin' doesn't match the size or checksum the zip records for it; the zip is damaged."],
            Lines(Validate(path)));
    }

    [Fact]
    public void An_entry_that_appears_twice_is_refused()
    {
        // Windows names are case-insensitive, so these are one file twice.
        Assert.Equal([":0: error: Entry 'LAYOUT.yaml' appears in the zip more than once."],
            Lines(Validate(Zip(("layout.yaml", Layout), ("LAYOUT.yaml", Layout)))));
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_an_error()
    {
        var path = _folder.File("theme.zip");
        File.WriteAllText(path, "not a zip");

        var issue = Assert.Single(Validate(path).Issues);

        Assert.Equal(("", 0), (issue.File, issue.Line));
        Assert.StartsWith("The zip file could not be read: ", issue.Message);
    }

    [Fact]
    public void A_missing_zip_is_an_error()
    {
        Assert.Equal([":0: error: The zip file does not exist."], Lines(Validate(_folder.File("gone.zip"))));
    }

    [Fact]
    public void The_temporary_folder_is_deleted_afterwards()
    {
        var path = Zip(("neon/layout.yaml", Layout), ("neon/images/a.png", "x"));
        var issues = new List<ThemeIssue>();

        string folder;
        using (var zip = ThemeZip.Extract(path, ThemeLimits.Default, issues))
        {
            folder = zip.ThemeFolder!;
            Assert.True(File.Exists(Path.Combine(folder, "images", "a.png")));
            Assert.StartsWith(Path.GetTempPath(), folder);
        }

        Assert.Empty(issues);
        Assert.False(Directory.Exists(folder));
        Assert.False(Directory.Exists(Path.GetDirectoryName(folder)));
    }
}
