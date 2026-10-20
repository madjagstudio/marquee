using System.Runtime.CompilerServices;
using System.Text;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Validation;
using Marquee.Core.Video;
using SkiaSharp;
using static Marquee.Core.Tests.TestSupport.Validations;

namespace Marquee.Core.Tests;

/// `marquee validate` on a theme folder: every problem the parser and the asset loaders find, with its file and line,
/// plus the checks only a shared theme needs (sizes, file count, links).
public class ThemeValidatorTests
{
    const string Canvas = "canvas: { orientation: landscape }\n";
    const string Cheap = "half4 main(float2 p) { return half4(0, 0, 1, 1); }";

    static readonly ThemeLimits OneMegabyte = ThemeLimits.Default with { MaxFileBytes = 1024 * 1024 };

    static string ExampleTheme(string name, [CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "themes", name));

    static byte[] Gif() => GifWriter.Write(16, 4, [SKColors.Red, SKColors.Lime],
        GifFrame.Solid(SKRectI.Create(16, 4), 0, 10), GifFrame.Solid(SKRectI.Create(16, 4), 1, 10));

    static ShaderProbe ShaderCosts(double milliseconds) => (_, _, _) => TimeSpan.FromMilliseconds(milliseconds);

    static VideoProbe VideoCosts(double milliseconds) =>
        (_, _) => new DecodeCost(TimeSpan.FromMilliseconds(milliseconds), TimeSpan.FromMilliseconds(33));

    [Fact]
    public void A_clean_theme_has_no_issues_and_reports_its_canvas()
    {
        using var folder = new TempTheme("canvas: { orientation: portrait, fps: 20 }\nbackground:\n  - fill: \"#203040\"\n");

        var report = Validate(folder.Folder);

        Assert.Empty(report.Issues);
        Assert.Equal(Path.GetFileName(folder.Folder), report.Name);
        Assert.Equal(new CanvasSpec(ThemeOrientation.Portrait, 20), report.Canvas);
    }

    [Fact]
    public void A_missing_folder_is_one_error()
    {
        var missing = Path.Combine(Path.GetTempPath(), "marquee-missing-" + Guid.NewGuid().ToString("N")[..8]);

        var report = Validate(missing);

        Assert.Equal([":0: error: The theme folder does not exist."], Lines(report));
        Assert.Equal(Path.GetFileName(missing), report.Name);
        Assert.Null(report.Canvas);
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_one_error()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("layout.yaml"), Canvas);

        var report = Validate(folder.File("layout.yaml"));

        Assert.Equal([":0: error: This is neither a theme folder nor a .zip of one."], Lines(report));
    }

    [Fact]
    public void A_folder_without_layout_yaml_is_one_error()
    {
        using var folder = new TempFolder();

        var report = Validate(folder.Path);

        Assert.Equal(["layout.yaml:0: error: The theme folder has no layout.yaml."], Lines(report));
    }

    [Fact]
    public void An_empty_layout_is_one_error()
    {
        using var folder = new TempTheme("");

        var report = Validate(folder.Folder);

        Assert.Equal(["layout.yaml:0: error: layout.yaml is empty. It needs at least a canvas section."], Lines(report));
        Assert.Null(report.Canvas);
    }

    [Theory]
    [InlineData("canvas:\n  orientation: landscape\n fps: 30\n", 3, "YAML syntax error:")]
    [InlineData("canvas: { orientation: landscape }\nwidget: []\n", 2, "Unknown key 'widget' in layout.yaml.")]
    [InlineData("canvas: { orientation: landscape }\nbackground:\n  - fill: \"#GG0000\"\n", 3, "fill: '#GG0000' is not a color.")]
    public void A_layout_error_is_reported_with_its_line(string layout, int line, string message)
    {
        using var folder = new TempTheme(layout);

        var issue = Assert.Single(Validate(folder.Folder).Issues);

        Assert.Equal((IssueSeverity.Error, "layout.yaml", line), (issue.Severity, issue.File, issue.Line));
        Assert.StartsWith(message, issue.Message);
    }

    [Fact]
    public void Every_independent_problem_is_reported_by_file_then_line()
    {
        using var folder = new TempTheme(Canvas + """
            styles:
              big: { size: huge }
            background:
              - fill: "#GG0000"
              - image: images/gone.png
              - shader: shaders/broken.sksl
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, smooth: -1 }
              - { type: image, at: [0, 0], size: [10, 10], src: logo.png }
            """);
        folder.AddFile("shaders/broken.sksl", Encoding.UTF8.GetBytes("half4 main(float2 p) { return nope; }"));

        var lines = Lines(Validate(folder.Folder));

        Assert.Equal(
            [
                "images/gone.png:0: error: Image 'images/gone.png' was not found and was skipped.",
                "layout.yaml:3: error: size must be a number, not 'huge'",
                "layout.yaml:5: error: fill: '#GG0000' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"",
                "layout.yaml:9: error: smooth must be between 0 and 10000",
                "logo.png:0: error: Image 'logo.png' was not found and was skipped.",
            ],
            lines.SkipLast(1));
        Assert.StartsWith("shaders/broken.sksl:0: error: Shader 'shaders/broken.sksl' doesn't compile: error:", lines[^1]);
    }

    [Fact]
    public void Ring_errors_are_reported_with_their_line()
    {
        using var folder = new TempTheme(Canvas + "ring:\n  effect: strobe\n  brightness: 120\n");

        Assert.Equal(
            [
                "layout.yaml:3: error: ring.effect must be one of: static, breathe, spin, rainbow; not 'strobe'",
                "layout.yaml:4: error: ring.brightness must be between 0 and 100",
            ],
            Lines(Validate(folder.Folder)));
    }

    [Fact]
    public void A_canvas_error_leaves_the_canvas_unknown_and_the_rest_still_checked()
    {
        using var folder = new TempTheme("canvas: { orientation: sideways }\nbackground:\n  - image: gone.png\n");

        var report = Validate(folder.Folder);

        Assert.Null(report.Canvas);
        Assert.Equal(
            [
                "gone.png:0: error: Image 'gone.png' was not found and was skipped.",
                "layout.yaml:1: error: canvas.orientation must be one of: portrait, landscape; not 'sideways'",
            ],
            Lines(report));
    }

    [Fact]
    public void An_asset_path_outside_the_folder_is_reported_on_layout_yaml()
    {
        using var folder = new TempTheme(Canvas + "background:\n  - image: ../outside.png\n  - image: \"bad\\0.png\"\n");

        Assert.Equal(
            [
                "layout.yaml:0: error: Image '../outside.png' is outside the theme folder and was skipped.",
                "layout.yaml:0: error: Image 'bad\0.png' is not a valid path and was skipped.",
            ],
            Lines(Validate(folder.Folder)));
    }

    [Fact]
    public void Asset_paths_are_reported_as_found_in_the_folder()
    {
        using var folder = new TempTheme(Canvas + "background:\n  - image: ./images\\gone.png\n");

        Assert.Equal(["images/gone.png:0: error: Image './images\\gone.png' was not found and was skipped."], Lines(Validate(folder.Folder)));
    }

    [Fact]
    public void A_shader_over_the_frame_budget_is_an_error_at_the_themes_fps_or_the_override()
    {
        using var folder = new TempTheme("canvas: { orientation: landscape, fps: 60 }\nbackground:\n  - shader: s.sksl\n");
        folder.AddFile("s.sksl", Encoding.UTF8.GetBytes(Cheap));

        Assert.Equal(
            ["s.sksl:0: error: Shader 's.sksl' would take about 20 ms to render a frame, over the 17 ms budget at 60 fps " +
                "(lower its scale or the fps, or simplify it); the layer is skipped."],
            Lines(Validate(folder.Folder, shaders: ShaderCosts(20))));
        Assert.Empty(Validate(folder.Folder, shaders: ShaderCosts(20), fps: 30).Issues);
    }

    [Fact]
    public void Unused_shader_bindings_are_warnings()
    {
        using var folder = new TempTheme(Canvas + "background:\n  - shader: s.sksl\n    uniforms: { glow: 1 }\n");
        folder.AddFile("s.sksl", Encoding.UTF8.GetBytes(Cheap));

        Assert.Equal(["s.sksl:0: warning: Shader 's.sksl' has no uniform 'glow'; that binding is ignored."], Lines(Validate(folder.Folder)));
    }

    [Fact]
    public void A_missing_font_file_is_an_error_and_a_family_not_installed_here_a_warning()
    {
        using var folder = new TempTheme(Canvas + """
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: a, style: { font: fonts/Gone.ttf } }
              - { type: text, at: [0, 0], size: [10, 10], text: b, style: { font: NoSuchFamily123 } }
            """);

        var report = Validate(folder.Folder);

        Assert.Equal(
            [
                "fonts/Gone.ttf:0: error: Font file 'fonts/Gone.ttf' was not found or could not be read; using Inter instead.",
                "layout.yaml:0: warning: Font 'NoSuchFamily123' is not installed on this PC; using Inter instead.",
            ],
            Lines(report));
        Assert.Equal((1, 1), (report.Errors, report.Warnings));
    }

    [Fact]
    public void A_clip_that_cannot_be_decoded_is_an_error()
    {
        using var folder = new TempTheme(Canvas + "background:\n  - video: clips/rain.gif\n");
        folder.AddFile("clips/rain.gif", "not a picture"u8.ToArray());

        Assert.Equal(
            ["clips/rain.gif:0: error: Video 'clips/rain.gif' was skipped: The clip is not a GIF or WebP image that can be read."],
            Lines(Validate(folder.Folder)));
    }

    [Fact]
    public void A_clip_that_cannot_keep_up_is_an_error()
    {
        using var folder = new TempTheme(Canvas + "background:\n  - video: clips/rain.gif\n");
        folder.AddFile("clips/rain.gif", Gif());

        Assert.Equal(
            ["clips/rain.gif:0: error: Video 'clips/rain.gif' was skipped: Decoding a frame takes about 45 ms, longer than the 33 ms " +
                "each frame is shown, so playback can't keep up. Use a smaller clip, or one with fewer frames per second."],
            Lines(Validate(folder.Folder, videos: VideoCosts(45))));
    }

    [Fact]
    public void A_file_over_the_size_limit_is_an_error_naming_it()
    {
        // The real limit is 200 MB; a smaller one exercises the same check.
        Assert.Equal(200L * 1024 * 1024, ThemeLimits.Default.MaxFileBytes);
        using var folder = new TempTheme(Canvas);
        folder.AddFile("extras/big.bin", new byte[1024 * 1024 + 1]);
        folder.AddFile("extras/small.bin", new byte[1024 * 1024]);

        Assert.Equal(
            ["extras/big.bin:0: error: The file is 2 MB, over the 1 MB limit for a file in a theme."],
            Lines(Validate(folder.Folder, OneMegabyte)));
    }

    [Fact]
    public void A_theme_over_the_total_size_limit_is_an_error()
    {
        Assert.Equal(512L * 1024 * 1024, ThemeLimits.Default.MaxTotalBytes);
        using var folder = new TempTheme(Canvas);
        folder.AddFile("a.bin", new byte[600 * 1024]);
        folder.AddFile("sub/b.bin", new byte[600 * 1024]);

        Assert.Equal(
            [":0: error: The theme's files add up to 2 MB, over the 1 MB limit for a theme."],
            Lines(Validate(folder.Folder, ThemeLimits.Default with { MaxTotalBytes = 1024 * 1024 })));
    }

    [Fact]
    public void A_theme_with_too_many_files_is_an_error()
    {
        Assert.Equal(1000, ThemeLimits.Default.MaxFiles);
        using var folder = new TempTheme(Canvas);
        foreach (var name in new[] { "a.txt", "b/c.txt", "b/d/e.txt" })
            folder.AddFile(name, [1]);

        Assert.Equal(
            [":0: error: The theme has more than 3 files, the limit for a theme."],
            Lines(Validate(folder.Folder, ThemeLimits.Default with { MaxFiles = 3 })));
        Assert.Empty(Validate(folder.Folder, ThemeLimits.Default with { MaxFiles = 4 }).Issues);
    }

    [Fact]
    public void A_junction_is_an_error_and_is_not_followed()
    {
        using var folder = new TempTheme(Canvas);
        using var outside = new TempFolder();
        for (var i = 0; i < 5; i++) File.WriteAllBytes(outside.File($"{i}.bin"), new byte[1024 * 1024 + 1]);
        var link = Path.Combine(folder.Folder, "linked");
        Links.MakeJunction(link, outside.Path);
        try
        {
            // Followed, the junction would bring five files over both limits; only the link itself is reported.
            var report = Validate(folder.Folder, OneMegabyte with { MaxFiles = 3 });

            Assert.Equal(["linked:0: error: This is a symbolic link or junction, which a shared theme can't contain; it was not followed."],
                Lines(report));
        }
        finally
        {
            // .NET's recursive delete removes a junction with DeleteVolumeMountPoint, which is refused without admin
            // rights; removing it as a directory first works for anyone.
            Directory.Delete(link);
        }
    }

    [Fact]
    public void A_symbolic_link_is_an_error()
    {
        using var folder = new TempTheme(Canvas);
        using var outside = new TempFolder();
        File.WriteAllText(outside.File("secret.txt"), "x");
        try
        {
            File.CreateSymbolicLink(Path.Combine(folder.Folder, "alias.txt"), outside.File("secret.txt"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows creates symbolic links only for administrators or with developer mode on. The junction test covers
            // the same check (both are reparse points with a link target) on any machine.
            return;
        }

        Assert.Equal(["alias.txt:0: error: This is a symbolic link or junction, which a shared theme can't contain; it was not followed."],
            Lines(Validate(folder.Folder)));
    }

    /// The asset loaders don't go through a link either. Each file behind this one is unreadable, so any that was
    /// opened would be reported as well.
    [Fact]
    public void Assets_behind_a_junction_are_not_read()
    {
        using var folder = new TempTheme(Canvas + """
            background:
              - image: linked/logo.png
              - shader: linked/s.sksl
              - video: linked/rain.gif
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { font: linked/f.ttf } }
            """);
        using var outside = new TempFolder();
        foreach (var name in new[] { "logo.png", "s.sksl", "rain.gif", "f.ttf" })
            File.WriteAllText(outside.File(name), "not what its name says");
        var link = Path.Combine(folder.Folder, "linked");
        Links.MakeJunction(link, outside.Path);
        try
        {
            var probed = 0;
            var report = Validate(folder.Folder, videos: (_, _) =>
            {
                probed++;
                return new DecodeCost(TimeSpan.Zero, TimeSpan.Zero);
            });

            Assert.Equal(["linked:0: error: This is a symbolic link or junction, which a shared theme can't contain; it was not followed."],
                Lines(report));
            Assert.Equal(0, probed);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    /// A file over the limit is reported once, for its size, and not read: an image's header would otherwise be.
    [Fact]
    public void Assets_over_the_file_size_limit_are_not_read()
    {
        using var folder = new TempTheme(Canvas + "background:\n  - image: big.png\n  - video: clips/big.gif\n");
        folder.AddFile("big.png", new byte[1024 * 1024 + 1]);
        folder.AddFile("clips/big.gif", new byte[1024 * 1024 + 1]);

        Assert.Equal(
            [
                "big.png:0: error: The file is 2 MB, over the 1 MB limit for a file in a theme.",
                "clips/big.gif:0: error: The file is 2 MB, over the 1 MB limit for a file in a theme.",
            ],
            Lines(Validate(folder.Folder, OneMegabyte)));
    }

    [Theory]
    [InlineData("example-landscape", ThemeOrientation.Landscape)]
    [InlineData("example-portrait", ThemeOrientation.Portrait)]
    [InlineData("example-agents", ThemeOrientation.Landscape)]
    [InlineData("example-showcase", ThemeOrientation.Landscape)]
    public void The_example_themes_are_clean(string name, ThemeOrientation orientation)
    {
        // With the free shader probe: whether example-agents' aurora fits the frame budget depends on the machine (a slow
        // CI runner may skip it by design), and ShaderCostTests covers the check itself.
        var report = Validate(ExampleTheme(name));

        Assert.Empty(report.Issues);
        Assert.Equal(name, report.Name);
        Assert.Equal(new CanvasSpec(orientation, 30), report.Canvas);
    }
}
