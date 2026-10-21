using Marquee.Editor.Resources;
using Marquee.Editor.Startup;
using Marquee.Editor.Tests.TestSupport;
using Marquee.Host;

namespace Marquee.Editor.Tests;

/// Which theme the editor opens: the one its command line names, else the one config.yaml names (the one on the
/// screen), else the tray's default example.
public class EditorArgumentsTests
{
    [Fact]
    public void A_theme_name_on_the_command_line_is_found_in_the_theme_folders()
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");

        var target = EditorArguments.Resolve(["mine"], environment.Environment.Locator, environment.ConfigFile);

        Assert.Equal(mine, target.Folder);
    }

    [Fact]
    public void A_path_on_the_command_line_is_from_the_current_folder()
    {
        using var environment = new TestEnvironment();
        var elsewhere = Path.Combine(environment.Root, "work", "draft");
        Directory.CreateDirectory(elsewhere);
        var locator = new ThemeLocator([environment.UserThemes], Path.Combine(environment.Root, "work"));

        var target = EditorArguments.Resolve([@".\draft"], locator, environment.ConfigFile);

        Assert.Equal(elsewhere, target.Folder);
    }

    [Fact]
    public void With_no_arguments_it_opens_the_theme_config_yaml_names()
    {
        using var environment = new TestEnvironment();
        environment.AddTheme("example-landscape", installed: true);
        var mine = environment.AddTheme("mine");
        File.WriteAllText(environment.ConfigFile, "# my settings\ntheme: mine\nfps: 30\n");

        var target = EditorArguments.Resolve([], environment.Environment.Locator, environment.ConfigFile);

        Assert.Equal(mine, target.Folder);
    }

    [Fact]
    public void A_relative_theme_in_config_yaml_is_from_config_yamls_folder_as_the_tray_reads_it()
    {
        using var environment = new TestEnvironment();
        var beside = Path.Combine(Path.GetDirectoryName(environment.ConfigFile)!, "beside");
        Directory.CreateDirectory(beside);
        File.WriteAllText(environment.ConfigFile, "theme: ./beside\n");

        var target = EditorArguments.Resolve([], environment.Environment.Locator, environment.ConfigFile);

        Assert.Equal(beside, target.Folder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fps: 30\n")]
    [InlineData("theme: [unclosed\n")]
    public void Without_a_theme_in_config_yaml_it_opens_the_default_example(string? config)
    {
        using var environment = new TestEnvironment();
        var example = environment.AddTheme(EditorArguments.DefaultTheme, installed: true);
        if (config is not null) File.WriteAllText(environment.ConfigFile, config);

        var target = EditorArguments.Resolve([], environment.Environment.Locator, environment.ConfigFile);

        Assert.Equal(example, target.Folder);
    }

    [Fact]
    public void A_theme_that_isnt_there_is_an_error_saying_where_it_looked()
    {
        using var environment = new TestEnvironment();

        var target = EditorArguments.Resolve(["nowhere"], environment.Environment.Locator, environment.ConfigFile);

        Assert.Null(target.Folder);
        Assert.Contains("nowhere", target.Error);
        Assert.Contains(environment.UserThemes, target.Error);
    }

    [Fact]
    public void More_than_one_argument_says_how_to_start_it()
    {
        using var environment = new TestEnvironment();
        environment.AddTheme("mine");

        var target = EditorArguments.Resolve(["mine", "extra"], environment.Environment.Locator, environment.ConfigFile);

        Assert.Equal(ThemeTarget.Failed(Strings.Usage), target);
    }
}
