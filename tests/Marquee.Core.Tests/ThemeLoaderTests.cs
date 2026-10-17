using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class ThemeLoaderTests
{
    const string Canvas = "canvas: { orientation: landscape }\n";

    [Fact]
    public void Loads_the_layout_and_its_images()
    {
        using var folder = new TempTheme(Canvas + """
            background:
              - image: art/sky.png
            widgets:
              - { type: image, at: [0, 0], size: [32, 32], src: icons/cpu.png }
            """);
        folder.AddImage("art/sky.png", SKColors.Blue, 320, 80);
        folder.AddImage("icons/cpu.png", SKColors.Red);

        using var theme = folder.Load();

        Assert.Equal(Path.GetFileName(folder.Folder), theme.Name);
        Assert.Equal(ThemeOrientation.Landscape, theme.Spec.Canvas.Orientation);
        Assert.Equal(320, theme.Assets.Image("art/sky.png")!.Width);
        Assert.Equal(SKColors.Red, Images.PixelAt(theme.Assets.Image("icons/cpu.png")!, 5, 5));
        Assert.Empty(theme.Warnings);
    }

    [Fact]
    public void Images_inside_groups_are_loaded_too()
    {
        using var folder = new TempTheme(Canvas + """
            widgets:
              - type: group
                at: [0, 0]
                size: [100, 100]
                children:
                  - { type: image, at: [0, 0], size: [32, 32], src: deep.png }
            """);
        folder.AddImage("deep.png", SKColors.Green);

        using var theme = folder.Load();

        Assert.NotNull(theme.Assets.Image("deep.png"));
    }

    [Fact]
    public void Missing_and_broken_images_become_warnings()
    {
        using var folder = new TempTheme(Canvas + """
            background:
              - image: missing.png
              - image: broken.png
              - image: ../outside.png
            """);
        folder.AddFile("broken.png", [1, 2, 3, 4]);

        using var theme = folder.Load();

        Assert.Null(theme.Assets.Image("missing.png"));
        Assert.Null(theme.Assets.Image("broken.png"));
        Assert.Equal(
            [
                "Image 'missing.png' was not found and was skipped.",
                "Image 'broken.png' could not be read and was skipped.",
                "Image '../outside.png' is outside the theme folder and was skipped.",
            ],
            theme.Warnings);
    }

    [Fact]
    public void Default_font_is_the_bundled_inter()
    {
        using var folder = new TempTheme(Canvas);
        using var theme = folder.Load();

        Assert.Same(BundledFonts.Regular, theme.Assets.Typeface(null, 400));
        Assert.Same(BundledFonts.Bold, theme.Assets.Typeface("Inter", 700));
        Assert.Equal("Inter", BundledFonts.Regular.FamilyName);
        Assert.True(BundledFonts.Bold.FontWeight >= 600);
    }

    [Fact]
    public void Font_files_load_from_the_theme_folder()
    {
        using var folder = new TempTheme(Canvas + """
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { font: fonts/Mine.ttf } }
            """);
        folder.AddFile("fonts/Mine.ttf", BundledFonts.ReadBytes("Inter-Bold.ttf"));

        using var theme = folder.Load();
        var typeface = theme.Assets.Typeface("fonts/Mine.ttf", 400);

        Assert.NotSame(BundledFonts.Bold, typeface);
        Assert.Equal(BundledFonts.Bold.FamilyName, typeface.FamilyName);
        Assert.Empty(theme.Warnings);
    }

    [Fact]
    public void Missing_fonts_fall_back_to_inter_with_a_warning()
    {
        using var folder = new TempTheme(Canvas + """
            widgets:
              - type: value
                at: [0, 0]
                size: [10, 10]
                source: cpu.temp
                style: { font: fonts/Gone.ttf }
                when: [{ above: 1, style: { font: NoSuchFamily123 } }]
            """);

        using var theme = folder.Load();

        Assert.Same(BundledFonts.Regular, theme.Assets.Typeface("fonts/Gone.ttf", 400));
        Assert.Same(BundledFonts.Bold, theme.Assets.Typeface("NoSuchFamily123", 700));
        Assert.Equal(
            [
                "Font file 'fonts/Gone.ttf' was not found or could not be read; using Inter instead.",
                "Font 'NoSuchFamily123' is not installed on this PC; using Inter instead.",
            ],
            theme.Warnings);
    }

    [Fact]
    public void Installed_families_are_used_by_name()
    {
        using var folder = new TempTheme(Canvas);
        using var theme = folder.Load();

        Assert.Equal("Arial", theme.Assets.Typeface("Arial", 400).FamilyName);
    }

    [Fact]
    public void Missing_folder_or_layout_is_a_theme_error()
    {
        var nowhere = Path.Combine(Path.GetTempPath(), "marquee-nowhere-" + Guid.NewGuid());
        Assert.Contains("does not exist", Assert.Throws<ThemeException>(() => ThemeLoader.Load(nowhere)).Message);

        using var folder = new TempTheme(Canvas);
        File.Delete(folder.LayoutPath);
        Assert.Contains("has no layout.yaml", Assert.Throws<ThemeException>(folder.Load).Message);
    }

    [Fact]
    public void Layout_errors_keep_their_line()
    {
        using var folder = new TempTheme(Canvas + "widgets:\n  - type: dial\n");

        var ex = Assert.Throws<ThemeException>(folder.Load);

        Assert.Equal(3, ex.Line);
    }

    [Fact]
    public void Dispose_releases_assets_once()
    {
        using var folder = new TempTheme(Canvas + "background:\n  - image: a.png\n");
        folder.AddImage("a.png", SKColors.Red);
        var theme = folder.Load();

        theme.Dispose();
        theme.Dispose();

        Assert.True(theme.IsDisposed);
        Assert.Null(theme.Assets.Image("a.png"));
    }

    [Fact]
    public void Disposing_one_theme_leaves_another_themes_installed_font_usable()
    {
        using var firstFolder = new TempTheme(Canvas);
        using var secondFolder = new TempTheme(Canvas);
        var first = firstFolder.Load();
        using var second = secondFolder.Load();
        var kept = second.Assets.Typeface("Arial", 400);
        first.Assets.Typeface("Arial", 400);

        first.Dispose();

        Assert.NotEqual(IntPtr.Zero, kept.Handle);
        using var font = new SKFont(kept, 24);
        Assert.True(font.MeasureText("Marquee") > 0);
    }

    [Fact]
    public void Loading_a_theme_does_not_dispose_a_live_themes_installed_font()
    {
        using var liveFolder = new TempTheme(Canvas);
        using var live = liveFolder.Load();
        var typeface = live.Assets.Typeface("Arial", 400);

        using var nextFolder = new TempTheme(Canvas + """
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { font: Arial } }
            """);
        using var next = nextFolder.Load();

        Assert.NotEqual(IntPtr.Zero, typeface.Handle);
        using var font = new SKFont(typeface, 24);
        Assert.True(font.MeasureText("Marquee") > 0);
    }
}
