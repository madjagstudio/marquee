using System.Runtime.CompilerServices;
using Marquee.Core.Themes;
using Marquee.Editor.Start;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class StartScreenTests
{
    [Theory]
    [InlineData("start-blank")]
    [InlineData("start-gaming")]
    [InlineData("start-monitor")]
    public void Every_start_loads_clean(string name)
    {
        using var theme = ThemeLoader.Load(Folder(name));
        Assert.Empty(theme.Warnings);
    }

    [Theory]
    [InlineData("landscape", ThemeOrientation.Landscape)]
    [InlineData("portrait", ThemeOrientation.Portrait)]
    public void Choosing_gaming_copies_the_named_theme_in_the_screens_orientation(string orientation, ThemeOrientation expected) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        environment.AddExample("start-gaming", installed: true);
        File.WriteAllText(environment.ConfigFile, "orientation: " + orientation + "\n");
        using var start = new StartViewModel(environment.Environment);
        var folder = start.Create("start-gaming", "My game screen");
        using var editor = new OpenEditor(environment, folder);
        Assert.Equal("My game screen", editor.Model.ThemeName);
        Assert.Equal(expected, editor.Session.Theme!.Spec.Canvas.Orientation);
        Assert.Equal(3, editor.Session.Widgets.Count);
        Assert.Empty(editor.Session.Theme.Warnings);
        Assert.StartsWith(environment.UserThemes, folder);
        using var original = ThemeLoader.Load(Path.Combine(environment.InstalledThemes, "start-gaming"));
        Assert.Equal(ThemeOrientation.Landscape, original.Spec.Canvas.Orientation);
    });

    static string Folder(string name, [CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../themes", name));
}
