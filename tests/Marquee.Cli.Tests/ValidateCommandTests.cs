using System.IO.Compression;
using Marquee.Cli.Tests.TestSupport;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Validation;
using Marquee.Host;

namespace Marquee.Cli.Tests;

/// `marquee validate`: one line per problem, sorted by file then line, a summary naming the theme's orientation and
/// size, and the exit code a script or the gallery can act on. These capture the console, so they run in its collection.
[Collection(nameof(ConsoleCapture))]
public class ValidateCommandTests
{
    const string Shader = "half4 main(float2 p) { return half4(0, 0, 1, 1); }";

    /// Without the shader and video cost checks, which depend on the machine; ValidateCommand passes them through.
    static readonly ThemeValidator Free = Validator(shaderMs: 0);

    static ThemeValidator Validator(double shaderMs) => new(
        (_, _, _) => TimeSpan.FromMilliseconds(shaderMs),
        (_, _) => new DecodeCost(TimeSpan.Zero, TimeSpan.Zero),
        ThemeLimits.Default);

    static (int? Exit, string[] Lines) Run(ThemeValidator validator, params string[] args)
    {
        using var console = new ConsoleCapture();
        var exit = ValidateCommand.Run(args, validator, new ThemeLocator([], Environment.CurrentDirectory));
        Assert.Equal("", console.Error);
        return (exit, console.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void A_clean_theme_prints_its_summary_and_exits_0()
    {
        using var theme = new TempTheme("canvas: { orientation: portrait }\n");

        var (exit, lines) = Run(Free, theme.Folder);

        Assert.Equal(0, exit);
        Assert.Equal([$"{theme.Name} (portrait, 480×1920): no problems found."], lines);
    }

    [Fact]
    public void Problems_are_printed_one_a_line_by_file_then_line_and_errors_exit_1()
    {
        using var theme = new TempTheme("""
            canvas: { orientation: landscape }
            widget: []
            background:
              - image: b.png
              - fill: nope
              - image: a.png
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { font: NoSuchFamily123 } }
            """);

        var (exit, lines) = Run(Free, theme.Folder);

        Assert.Equal(1, exit);
        Assert.Equal(
            [
                "a.png: error: Image 'a.png' was not found and was skipped.",
                "b.png: error: Image 'b.png' was not found and was skipped.",
                "layout.yaml:2: error: Unknown key 'widget' in layout.yaml. Expected one of: canvas, styles, background, widgets",
                "layout.yaml:5: error: fill: 'nope' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"",
                "layout.yaml: warning: Font 'NoSuchFamily123' is not installed on this PC; using Inter instead.",
                $"{theme.Name} (landscape, 1920×480): 4 errors, 1 warning.",
            ],
            lines);
    }

    [Fact]
    public void Warnings_alone_exit_0()
    {
        using var theme = new TempTheme("""
            canvas: { orientation: landscape }
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { font: NoSuchFamily123 } }
            """);

        var (exit, lines) = Run(Free, theme.Folder);

        Assert.Equal(0, exit);
        Assert.Equal($"{theme.Name} (landscape, 1920×480): 1 warning.", lines[^1]);
    }

    [Fact]
    public void One_error_and_several_warnings_are_counted_in_words()
    {
        using var theme = new TempTheme("""
            canvas: { orientation: landscape }
            background:
              - image: gone.png
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: a, style: { font: NoSuchFamily123 } }
              - { type: text, at: [0, 0], size: [10, 10], text: b, style: { font: NoSuchFamily456 } }
            """);

        Assert.Equal($"{theme.Name} (landscape, 1920×480): 1 error, 2 warnings.", Run(Free, theme.Folder).Lines[^1]);
    }

    [Fact]
    public void Fps_sets_the_shader_budget()
    {
        using var theme = new TempTheme("canvas: { orientation: landscape }\nbackground:\n  - shader: s.sksl\n");
        theme.AddFile("s.sksl", Shader);
        var twentyMs = Validator(shaderMs: 20);

        Assert.Equal(0, Run(twentyMs, theme.Folder, "--fps", "30").Exit);

        var (exit, lines) = Run(twentyMs, theme.Folder, "--fps", "60");
        Assert.Equal(1, exit);
        Assert.StartsWith("s.sksl: error: Shader 's.sksl' would take about 20 ms to render a frame, over the 17 ms budget at 60 fps", lines[0]);
    }

    [Fact]
    public void A_problem_with_the_theme_as_a_whole_is_shown_on_the_path_given()
    {
        var missing = Path.Combine(Path.GetTempPath(), "marquee-missing-" + Guid.NewGuid().ToString("N")[..8]);

        var (exit, lines) = Run(Free, missing);

        Assert.Equal(1, exit);
        Assert.Equal([$"{missing}: error: The theme folder does not exist.", $"{Path.GetFileName(missing)}: 1 error."], lines);
    }

    [Fact]
    public void A_zipped_theme_validates_like_a_folder()
    {
        using var folder = new TempTheme("canvas: { orientation: landscape }\n");
        var zip = Path.Combine(folder.Folder, "neon.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var layout = new StreamWriter(archive.CreateEntry("neon/layout.yaml").Open()))
            layout.Write("canvas: { orientation: portrait }\nbackground:\n  - image: logo.png\n");

        var (exit, lines) = Run(Free, zip);

        Assert.Equal(1, exit);
        Assert.Equal(["logo.png: error: Image 'logo.png' was not found and was skipped.", "neon (portrait, 480×1920): 1 error."], lines);
    }

    /// A theme is someone else's text. Printed as it is, an escape sequence in a key or in a zip entry's name could
    /// rewrite the terminal, erasing the error lines above it, say. Control characters (C0, DEL, C1) and the
    /// bidirectional overrides, which reorder what is shown, are printed as \uXXXX instead.
    [Fact]
    public void Control_characters_in_layout_yaml_are_printed_as_codes()
    {
        using var theme = new TempTheme("canvas: { orientation: landscape }\n\"\\e[1A\\e[2K\\u009B2K\\x7F\\u202Eok\\t\": 1\n");

        var (exit, lines) = Run(Free, theme.Folder);

        Assert.Equal(1, exit);
        Assert.Equal(
            "layout.yaml:2: error: Unknown key '\\u001B[1A\\u001B[2K\\u009B2K\\u007F\\u202Eok\\u0009' in layout.yaml. " +
            "Expected one of: canvas, styles, background, widgets",
            lines[0]);
    }

    [Fact]
    public void Control_characters_in_a_zips_entry_names_are_printed_as_codes()
    {
        using var folder = new TempTheme("canvas: { orientation: landscape }\n");
        var zip = Path.Combine(folder.Folder, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var layout = new StreamWriter(archive.CreateEntry("neon/layout.yaml").Open()))
                layout.Write("canvas: { orientation: portrait }\n");
            archive.CreateEntry("neon/\u001b]0;title\u0007\u001b[2Klogo.png").Open().Dispose();
        }

        var (exit, lines) = Run(Free, zip);

        Assert.Equal(1, exit);
        Assert.Equal(
            [
                $"{zip}: error: Entry 'neon/\\u001B]0;title\\u0007\\u001B[2Klogo.png' is not a valid file name on Windows.",
                "evil: 1 error.",
            ],
            lines);
    }

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    [InlineData("example-agents")]
    public void The_example_themes_are_clean(string name)
    {
        // With the free cost probes: whether example-agents' aurora fits the frame budget depends on the machine.
        var (exit, lines) = Run(Free, SourceTree.Path("themes", name));

        Assert.Equal(0, exit);
        Assert.EndsWith(": no problems found.", Assert.Single(lines));
    }

    [Theory]
    [InlineData]
    [InlineData("--fps", "30")]
    [InlineData("theme", "--fps")]
    [InlineData("theme", "--fps", "0")]
    [InlineData("theme", "--fps", "61")]
    [InlineData("theme", "--fps", "ten")]
    [InlineData("theme", "other")]
    [InlineData("theme", "--sink", "out")]
    public void Wrong_arguments_mean_usage(params string[] args)
    {
        var (exit, lines) = Run(Free, args);

        Assert.Null(exit);
        Assert.Empty(lines);
    }

    [Fact]
    public void Usage_shows_the_command()
    {
        Assert.Contains("marquee validate <theme-folder|zip> [--fps 1-60]", Strings.Usage);
    }

    [Fact]
    public void Its_words_come_from_the_resources()
    {
        using var theme = new TempTheme("canvas: { orientation: landscape }\nbackground:\n  - image: a.png\n");
        using var resources = SwappedResources.Pseudo();

        var (_, lines) = Run(Free, theme.Folder);

        Assert.Equal(
            [
                "[!a.png: error: Image 'a.png' was not found and was skipped.!]",
                $"[!{theme.Name} (landscape, 1920×480): [!1 error!].!]",
            ],
            lines);
    }

    [Fact]
    public void A_theme_name_is_found_where_marquee_run_finds_it()
    {
        using var theme = new TempTheme("canvas: { orientation: portrait }\n");
        var locator = new ThemeLocator([Path.GetDirectoryName(theme.Folder)!], Path.GetTempPath());

        using var console = new ConsoleCapture();
        var exit = ValidateCommand.Run([theme.Name], Free, locator);

        Assert.Equal(0, exit);
        Assert.Equal($"{theme.Name} (portrait, 480×1920): no problems found.", console.Out.Trim());
    }

    [Fact]
    public void A_name_found_nowhere_says_where_it_looked()
    {
        var locator = new ThemeLocator([Path.Combine(Path.GetTempPath(), "marquee-no-themes-here")], Path.GetTempPath());

        using var console = new ConsoleCapture();
        var exit = ValidateCommand.Run(["no-such-theme-4f2a"], Free, locator);

        Assert.Equal(1, exit);
        Assert.Contains("marquee-no-themes-here", console.Out);
    }
}
