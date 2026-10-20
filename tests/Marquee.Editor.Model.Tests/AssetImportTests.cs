using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Importing a file into a theme: checked as the theme loader would load it, copied into images\, fonts\ or video\
/// under a free name, and named by the forward-slash path layout.yaml takes. A refused file leaves nothing behind.
public class AssetImportTests
{
    sealed class Setup : IDisposable
    {
        public TempTheme Theme { get; } = TempTheme.With(Saving.Layout);

        /// Where the user's files come from.
        public TempFolder Downloads { get; } = new();

        public AssetImport Import(AssetChecks? checks = null, long maxBytes = AssetImport.MaxBytes) =>
            new(Theme.Folder, checks ?? Assets.Checks, maxBytes);

        public string Download(string name) => Downloads.Combine(name);

        public string InTheme(string relative) => Path.Combine(Theme.Folder, relative);

        /// Every file in the theme folder but layout.yaml, relative, with forward slashes: what an import left there.
        public IEnumerable<string> ThemeFiles() =>
            Directory.EnumerateFiles(Theme.Folder, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(Theme.Folder, file).Replace('\\', '/'))
                .Where(file => file != ThemeLoader.LayoutFileName)
                .Order(StringComparer.Ordinal);

        public void Dispose()
        {
            Theme.Dispose();
            Downloads.Dispose();
        }
    }

    [Fact]
    public void An_image_goes_into_images_and_is_named_with_forward_slashes()
    {
        using var setup = new Setup();
        var source = Assets.Png(setup.Download("logo.png"));

        var imported = setup.Import().Import(source, AssetKind.Image);

        Assert.Equal(new ImportedFile("images/logo.png", AssetKind.Image, Warning: null, Copied: true), imported);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(setup.InTheme(@"images\logo.png")));
        Assert.Equal(["images/logo.png"], setup.ThemeFiles());
    }

    [Fact]
    public void A_font_goes_into_fonts()
    {
        using var setup = new Setup();
        var source = Assets.Font(setup.Download("Display.ttf"));

        var imported = setup.Import().Import(source, AssetKind.Font);

        Assert.Equal(new ImportedFile("fonts/Display.ttf", AssetKind.Font, null, true), imported);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(setup.InTheme(@"fonts\Display.ttf")));
    }

    [Fact]
    public void A_clip_goes_into_video()
    {
        using var setup = new Setup();
        var source = Assets.Clip(setup.Download("intro.mp4"));

        var imported = setup.Import().Import(source, AssetKind.Video);

        Assert.Equal(new ImportedFile("video/intro.mp4", AssetKind.Video, null, true), imported);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(setup.InTheme(@"video\intro.mp4")));
    }

    [Fact]
    public void A_name_already_taken_gets_the_next_free_number()
    {
        using var setup = new Setup();
        var source = Assets.Png(setup.Download("logo.png"));
        var import = setup.Import();

        var first = import.Import(source, AssetKind.Image);
        var second = import.Import(source, AssetKind.Image);
        File.WriteAllText(setup.InTheme(@"images\logo-4.png"), "taken");
        Directory.CreateDirectory(setup.InTheme(@"images\logo-3.png")); // a folder takes a name too
        var third = import.Import(source, AssetKind.Image);

        Assert.Equal(["images/logo.png", "images/logo-2.png", "images/logo-5.png"], [first.Path, second.Path, third.Path]);
        Assert.Equal("taken", File.ReadAllText(setup.InTheme(@"images\logo-4.png")));
    }

    [Fact]
    public void A_file_over_the_size_limit_is_refused_before_it_is_read()
    {
        using var setup = new Setup();
        var source = Assets.Png(setup.Download("huge.png"));
        var limit = new FileInfo(source).Length - 1;

        var ex = Assert.Throws<AssetImportException>(() => setup.Import(maxBytes: limit).Import(source, AssetKind.Image));

        Assert.Contains("over the", ex.Reason);
        Assert.Contains("limit for importing", ex.Reason);
        Assert.Empty(setup.ThemeFiles());
    }

    /// Nothing imported can make the theme fail `marquee validate`, which refuses a file over the theme's per-file limit.
    [Fact]
    public void The_size_limit_is_the_themes_per_file_limit()
    {
        Assert.Equal(AssetImport.MaxBytes, Marquee.Core.Themes.Validation.ThemeLimits.Default.MaxFileBytes);
    }

    [Fact]
    public void A_corrupt_image_is_refused_with_a_plain_reason()
    {
        using var setup = new Setup();
        var source = setup.Download("broken.png");
        File.WriteAllBytes(source, [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3, 4, 5, 6, 7, 8]);

        var ex = Assert.Throws<AssetImportException>(() => setup.Import().Import(source, AssetKind.Image));

        Assert.Equal("It could not be read as an image. A theme can use PNG, JPEG, WebP or another format Skia reads.", ex.Reason);
        Assert.Empty(setup.ThemeFiles());
    }

    [Fact]
    public void An_image_wider_than_a_theme_can_show_is_refused()
    {
        using var setup = new Setup();
        var source = Assets.Png(setup.Download("wide.png"), width: 8193, height: 1);

        var ex = Assert.Throws<AssetImportException>(() => setup.Import().Import(source, AssetKind.Image));

        Assert.Equal("It is larger than 8192×8192 pixels, the largest image a theme can use.", ex.Reason);
    }

    [Fact]
    public void A_file_that_is_not_a_font_is_refused()
    {
        using var setup = new Setup();
        var source = setup.Download("notes.ttf");
        File.WriteAllText(source, "not a font");

        var ex = Assert.Throws<AssetImportException>(() => setup.Import().Import(source, AssetKind.Font));

        Assert.Equal("It could not be read as a font.", ex.Reason);
        Assert.Empty(setup.ThemeFiles());
    }

    [Fact]
    public void A_font_needs_an_extension_the_theme_loader_reads_as_a_font_file()
    {
        using var setup = new Setup();
        var source = Assets.Font(setup.Download("Display.woff"));

        var ex = Assert.Throws<AssetImportException>(() => setup.Import().Import(source, AssetKind.Font));

        Assert.Equal("A font file must be a .ttf, .otf or .ttc file.", ex.Reason);
    }

    [Fact]
    public void A_clip_needs_an_extension_the_parser_takes()
    {
        using var setup = new Setup();
        var source = Assets.Clip(setup.Download("intro.avi"));

        var ex = Assert.Throws<AssetImportException>(() => setup.Import().Import(source, AssetKind.Video));

        Assert.Equal("A clip must be a .mp4, .m4v, .mov, .wmv, .gif or .webp file.", ex.Reason);
    }

    [Fact]
    public void A_clip_that_does_not_decode_is_refused()
    {
        using var setup = new Setup();
        var source = setup.Download("intro.mp4");
        File.WriteAllText(source, "not a clip");

        var ex = Assert.Throws<AssetImportException>(() => setup.Import().Import(source, AssetKind.Video));

        Assert.NotEmpty(ex.Reason);
        Assert.Empty(setup.ThemeFiles());
    }

    [Fact]
    public void A_clip_too_slow_for_this_PC_is_imported_with_a_warning()
    {
        using var setup = new Setup();
        var source = Assets.Clip(setup.Download("intro.mp4"));

        var imported = setup.Import(Assets.SlowChecks).Import(source, AssetKind.Video);

        Assert.Equal("video/intro.mp4", imported.Path);
        Assert.True(File.Exists(setup.InTheme(@"video\intro.mp4")));
        Assert.StartsWith("On this PC the theme would skip it. Decoding a frame takes about 1,000 ms, longer than the 100 ms",
            imported.Warning);
    }

    [Fact]
    public void A_missing_file_is_refused()
    {
        using var setup = new Setup();

        var ex = Assert.Throws<AssetImportException>(() => setup.Import().Import(setup.Download("gone.png"), AssetKind.Image));

        Assert.Equal("The file was not found.", ex.Reason);
    }

    [Fact]
    public void A_file_already_in_the_theme_folder_is_used_where_it_is()
    {
        using var setup = new Setup();
        var source = Assets.Png(setup.InTheme(@"art\logo.png"));

        var imported = setup.Import().Import(source, AssetKind.Image);

        Assert.Equal(new ImportedFile("art/logo.png", AssetKind.Image, null, Copied: false), imported);
        Assert.Equal(["art/logo.png"], setup.ThemeFiles());
    }

    [Fact]
    public void A_junction_in_place_of_the_images_folder_is_refused_and_nothing_is_written_through_it()
    {
        using var setup = new Setup();
        using var elsewhere = new TempFolder();
        var source = Assets.Png(setup.Download("logo.png"));
        var link = setup.InTheme("images");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            var ex = Assert.Throws<AssetImportException>(() => setup.Import().Import(source, AssetKind.Image));

            Assert.Contains("junction", ex.Reason);
            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere.Path));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Theory]
    [InlineData("widgets[0].src", "type: image", AssetKind.Image)]
    [InlineData("widgets[0].src", "type: video", AssetKind.Video)]
    [InlineData("widgets[0].style.font", "type: text", AssetKind.Font)]
    [InlineData("widgets[0].spans[0].style.font", "type: text", AssetKind.Font)]
    [InlineData("background[0].image", "type: text", AssetKind.Image)]
    [InlineData("background[0].video", "type: text", AssetKind.Video)]
    [InlineData("background[0].shader", "type: text", null)]
    [InlineData("widgets[0].src", "type: text", null)]
    [InlineData("widgets[0].text", "type: text", null)]
    public void A_file_key_takes_its_kind_of_file(string key, string type, AssetKind? kind)
    {
        var document = LayoutDocument.Open($"background:\n  - fill: red\nwidgets:\n  - {{ {type}, at: [0, 0] }}\n");

        Assert.Equal(kind, AssetImport.KindFor(document, YamlPath.Parse(key)));
    }
}
