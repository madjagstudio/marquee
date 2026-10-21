using Marquee.Core.Config;
using Marquee.Editor.Resources;
using Marquee.Editor.Session;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

/// "Show on screen" writes `theme:` into config.yaml (a temp one here), changing nothing else in it.
public class ShowOnScreenTests
{
    [Fact]
    public void A_theme_in_the_theme_folders_is_written_by_name()
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");

        new ScreenTheme(environment.ConfigFile, environment.Environment.Locator).Show(mine);

        Assert.Equal("mine", ConfigParser.Parse(File.ReadAllText(environment.ConfigFile)).Theme);
    }

    [Fact]
    public void A_config_yaml_not_made_yet_is_made_with_its_folder()
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        var config = Path.Combine(environment.Root, "fresh", "Marquee", "config.yaml");

        new ScreenTheme(config, environment.Environment.Locator).Show(mine);

        Assert.Equal("mine", ConfigParser.Parse(File.ReadAllText(config)).Theme);
    }

    [Fact]
    public void Only_the_theme_changes_in_config_yaml()
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        File.WriteAllText(environment.ConfigFile, "# my settings\r\nfps: 30   # smooth\r\ntheme: example-landscape\r\n");

        new ScreenTheme(environment.ConfigFile, environment.Environment.Locator).Show(mine);

        Assert.Equal("# my settings\r\nfps: 30   # smooth\r\ntheme: mine\r\n", File.ReadAllText(environment.ConfigFile));
    }

    [Fact]
    public void A_theme_its_name_wouldnt_find_is_written_as_its_folder()
    {
        using var environment = new TestEnvironment();
        environment.AddTheme("twin");
        var shadowed = environment.AddTheme("twin", installed: true);
        var elsewhere = Path.Combine(environment.Root, "drafts", "draft");
        Directory.CreateDirectory(elsewhere);
        var screen = new ScreenTheme(environment.ConfigFile, environment.Environment.Locator);

        Assert.Equal(shadowed, screen.ValueFor(shadowed));
        Assert.Equal(elsewhere, screen.ValueFor(elsewhere));
    }

    [Fact]
    public void The_toolbar_button_writes_it_and_says_so() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));

        editor.Model.ShowOnScreenCommand.Execute(null);

        Assert.Equal("mine", ConfigParser.Parse(File.ReadAllText(environment.ConfigFile)).Theme);
        Assert.Equal(Strings.ShownOnScreen("mine"), editor.Model.Message);
    });

    [Fact]
    public void A_config_yaml_it_cant_change_is_reported_and_left_alone() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        File.WriteAllText(environment.ConfigFile, "theme: [unclosed\n");

        editor.Model.ShowOnScreen();

        Assert.Equal("theme: [unclosed\n", File.ReadAllText(environment.ConfigFile));
        Assert.StartsWith(Strings.ShowOnScreenFailed("").TrimEnd(), editor.Model.Message);
    });
}
