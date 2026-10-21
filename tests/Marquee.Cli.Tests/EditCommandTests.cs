using System.ComponentModel;
using System.Diagnostics;
using Marquee.Cli.Tests.TestSupport;
using Marquee.Host;

namespace Marquee.Cli.Tests;

/// `marquee edit`, with the elevation check, the theme folders, marquee.exe's folder and the starter all faked: no test
/// starts the editor. These capture the console, so they run in its collection.
[Collection(nameof(ConsoleCapture))]
public sealed class EditCommandTests : IDisposable
{
    /// Stands in for marquee.exe's folder, where the installer puts MarqueeEditor.exe, with a themes folder beside it as
    /// the installed examples have.
    readonly string _folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "marquee-edit-" + Guid.NewGuid().ToString("N")[..8])).FullName;

    readonly List<ProcessStartInfo> _started = [];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // %TEMP% is cleaned eventually.
        }
    }

    string Editor => Path.Combine(_folder, "MarqueeEditor.exe");

    string Themes => Path.Combine(_folder, "themes");

    /// The current folder a relative path is found from.
    string Here => Path.Combine(_folder, "here");

    string AddEditor()
    {
        File.WriteAllText(Editor, "");
        return Editor;
    }

    string AddTheme(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "layout.yaml"), "canvas: {}\n");
        return folder;
    }

    EditCommand.Machine Machine(bool elevated = false, Action<ProcessStartInfo>? start = null) =>
        new(() => elevated, new ThemeLocator([Themes], Here), Editor, start ?? _started.Add);

    static (int? Exit, string Out, string Error) Run(EditCommand.Machine machine, params string[] args)
    {
        using var console = new ConsoleCapture();
        var exit = EditCommand.Run(args, machine);
        return (exit, console.Out.Trim(), console.Error.Trim());
    }

    [Fact]
    public void With_no_theme_it_starts_the_editor_with_no_arguments_so_it_opens_the_one_config_yaml_names()
    {
        var editor = AddEditor();

        var (exit, output, error) = Run(Machine());

        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Equal("Opened the theme config.yaml names in the theme editor.", output);
        var start = Assert.Single(_started);
        Assert.Equal(editor, start.FileName);
        Assert.Empty(start.ArgumentList);
        Assert.Equal("", start.Arguments);
        Assert.False(start.UseShellExecute);
        Assert.Equal(_folder, start.WorkingDirectory);
    }

    [Fact]
    public void A_theme_name_is_found_as_marquee_run_finds_it_and_passed_as_its_full_path()
    {
        AddEditor();
        var neon = AddTheme(Path.Combine(Themes, "neon"));

        var (exit, output, _) = Run(Machine(), "neon");

        Assert.Equal(0, exit);
        Assert.Equal($"Opened {neon} in the theme editor.", output);
        Assert.Equal([neon], Assert.Single(_started).ArgumentList);
    }

    /// The editor starts in marquee.exe's folder, so a path relative to the terminal's folder is made full here.
    [Fact]
    public void A_relative_folder_is_found_from_the_current_folder()
    {
        AddEditor();
        var mine = AddTheme(Path.Combine(Here, "my themes", "mine"));

        var (exit, _, _) = Run(Machine(), @"my themes\mine");

        Assert.Equal(0, exit);
        var start = Assert.Single(_started);
        Assert.Equal([mine], start.ArgumentList);
        Assert.Equal(_folder, start.WorkingDirectory);
    }

    [Fact]
    public void A_theme_that_isnt_there_is_said_with_where_it_looked_and_nothing_starts()
    {
        AddEditor();

        var (exit, output, error) = Run(Machine(), "nowhere");

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Equal($"Theme 'nowhere' was not found. Marquee looked for a folder of that name in: {Themes}; {Here}", error);
        Assert.Empty(_started);
    }

    [Fact]
    public void From_an_elevated_terminal_it_refuses_and_says_to_use_a_normal_one()
    {
        AddEditor();
        AddTheme(Path.Combine(Themes, "neon"));

        var (exit, output, error) = Run(Machine(elevated: true), "neon");

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.StartsWith("This terminal is running as administrator, so marquee edit won't start the theme editor", error);
        Assert.EndsWith("Run marquee edit from a normal terminal.", error);
        Assert.Empty(_started);
    }

    [Fact]
    public void Without_the_editor_beside_marquee_exe_it_says_where_it_looked()
    {
        var (exit, _, error) = Run(Machine());

        Assert.Equal(1, exit);
        Assert.Contains(Editor, error);
        Assert.Empty(_started);
    }

    [Fact]
    public void An_editor_that_wont_start_is_said()
    {
        AddEditor();

        var (exit, _, error) = Run(Machine(start: _ => throw new Win32Exception(5)), []);

        Assert.Equal(1, exit);
        Assert.Equal($"Couldn't start the theme editor: {new Win32Exception(5).Message}", error);
    }

    [Theory]
    [InlineData("neon", "aurora")]
    [InlineData("--theme")]
    public void Anything_but_one_theme_is_a_usage_error(params string[] args)
    {
        AddEditor();

        Assert.Null(Run(Machine(), args).Exit);
        Assert.Empty(_started);
    }

    [Fact]
    public void The_real_editor_is_the_one_beside_marquee_exe() =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "MarqueeEditor.exe"), EditCommand.Machine.Real.EditorExe);

    [Fact]
    public void Usage_shows_the_command() =>
        Assert.Contains("marquee edit [<theme-folder>]", Strings.Usage);
}
