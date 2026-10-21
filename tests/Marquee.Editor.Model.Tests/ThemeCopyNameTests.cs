using Marquee.Core.Themes;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// "New from an example" copies an example under a name the user gives: the name is checked as a folder name in the
/// user's themes before anything is copied.
public class ThemeCopyNameTests
{
    [Fact]
    public void A_copy_is_made_under_the_name_given()
    {
        using var install = new TempFolder();
        using var data = new TempFolder();
        var example = Example(install.Combine("themes"), "example-showcase");
        var opening = new ThemeOpening(install.Combine("themes"), data.Combine("themes"));

        var copy = opening.MakeCopy(example, "  My dash  ");

        Assert.Equal(Path.Combine(data.Combine("themes"), "My dash"), copy);
        Assert.Equal(Saving.Layout, File.ReadAllText(Path.Combine(copy, ThemeLoader.LayoutFileName)));
        Assert.False(opening.Check(copy).IsReadOnly);
    }

    [Theory]
    [InlineData("", "Give the theme a name.")]
    [InlineData("   ", "Give the theme a name.")]
    [InlineData("a/b", "A theme's name can't have")]
    [InlineData("a:b", "A theme's name can't have")]
    [InlineData("what?", "A theme's name can't have")]
    [InlineData(".hidden", "can't start with a dot")]
    [InlineData("dash.", "can't end with a dot")]
    [InlineData("CON", "Windows keeps")]
    [InlineData("lpt1", "Windows keeps")]
    [InlineData("nul.txt", "Windows keeps")]
    public void A_name_windows_or_the_themes_folder_cant_take_is_refused(string name, string reason)
    {
        using var data = new TempFolder();
        var opening = new ThemeOpening(data.Combine("installed"), data.Combine("themes"));

        Assert.Contains(reason, opening.CopyNameProblem(name));
    }

    [Fact]
    public void A_name_too_long_or_taken_is_refused_and_nothing_is_copied()
    {
        using var install = new TempFolder();
        using var data = new TempFolder();
        var example = Example(install.Combine("themes"), "example-showcase");
        var opening = new ThemeOpening(install.Combine("themes"), data.Combine("themes"));
        Directory.CreateDirectory(data.Combine("themes", "Mine"));

        Assert.Contains("already a theme", opening.CopyNameProblem("mine"));
        Assert.Contains("at most", opening.CopyNameProblem(new string('a', ThemeOpening.MaxNameLength + 1)));
        Assert.Null(opening.CopyNameProblem("Mine 2"));
        var refused = Assert.Throws<ArgumentException>(() => opening.MakeCopy(example, "MINE"));

        Assert.Contains("already a theme", refused.Message);
        Assert.Single(Directory.GetFileSystemEntries(data.Combine("themes")));
    }

    static string Example(string parent, string name)
    {
        var folder = Path.Combine(parent, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ThemeLoader.LayoutFileName), Saving.Layout);
        return folder;
    }
}
