using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

public class ThemeLocatorTests
{
    /// The user's themes folder, the installed examples and a current directory, each in its own temp folder.
    sealed class Places : IDisposable
    {
        readonly TempDir _root = new();

        public Places()
        {
            User = _root.Folder("user-themes");
            Installed = _root.Folder("installed-themes");
            Current = _root.Folder("work");
            Locator = new ThemeLocator([User, Installed], Current);
        }

        public string User { get; }
        public string Installed { get; }
        public string Current { get; }
        public ThemeLocator Locator { get; }

        public string Theme(string folder, string name) => Directory.CreateDirectory(Path.Combine(folder, name)).FullName;

        public string Locate(string theme)
        {
            Assert.True(Locator.TryLocate(theme, out var folder, out var error), error);
            return folder;
        }

        public string Error(string theme)
        {
            Assert.False(Locator.TryLocate(theme, out _, out var error));
            return error;
        }

        public void Dispose() => _root.Dispose();
    }

    [Fact]
    public void A_name_is_looked_up_in_the_users_themes_first()
    {
        using var places = new Places();
        var user = places.Theme(places.User, "neon");
        places.Theme(places.Installed, "neon");
        places.Theme(places.Current, "neon");

        Assert.Equal(user, places.Locate("neon"));
    }

    [Fact]
    public void A_name_falls_back_to_the_installed_themes()
    {
        using var places = new Places();
        var installed = places.Theme(places.Installed, "example-agents");
        places.Theme(places.Current, "example-agents");

        Assert.Equal(installed, places.Locate("example-agents"));
    }

    [Fact]
    public void A_name_falls_back_to_a_folder_in_the_current_directory_for_a_dev_build()
    {
        using var places = new Places();
        var dev = places.Theme(places.Current, "work-in-progress");

        Assert.Equal(dev, places.Locate("work-in-progress"));
    }

    [Theory]
    [InlineData("themes/example-agents")]
    [InlineData(@"themes\example-agents")]
    [InlineData(@"themes\example-agents\")]
    public void A_relative_path_is_relative_to_the_current_directory_not_the_theme_folders(string theme)
    {
        using var places = new Places();
        var dev = places.Theme(Path.Combine(places.Current, "themes"), "example-agents");
        places.Theme(Path.Combine(places.User, "themes"), "example-agents");

        Assert.Equal(dev, places.Locate(theme));
    }

    [Fact]
    public void A_full_path_is_used_as_it_is()
    {
        using var places = new Places();
        using var elsewhere = new TempDir();
        var theme = elsewhere.Folder("mine");
        places.Theme(places.User, "mine");

        Assert.Equal(theme, places.Locate(theme));
    }

    [Fact]
    public void Dot_is_the_current_directory_not_a_name()
    {
        using var places = new Places();

        Assert.Equal(places.Current, places.Locate("."));
    }

    [Fact]
    public void A_path_that_is_not_a_folder_says_where_it_looked()
    {
        using var places = new Places();

        Assert.Equal($"There is no theme folder at {Path.Combine(places.Current, "themes", "nope")}.",
            places.Error("themes/nope"));
    }

    [Fact]
    public void A_name_that_is_nowhere_lists_every_place_it_looked_in_order()
    {
        using var places = new Places();

        Assert.Equal($"Theme 'nope' was not found. Marquee looked for a folder of that name in: {places.User}; {places.Installed}; {places.Current}",
            places.Error("nope"));
    }

    [Fact]
    public void A_file_with_the_themes_name_is_not_a_theme()
    {
        using var places = new Places();
        File.WriteAllText(Path.Combine(places.User, "neon"), "not a folder");
        var installed = places.Theme(places.Installed, "neon");

        Assert.Equal(installed, places.Locate("neon"));
    }

    [Fact]
    public void A_path_windows_rejects_is_an_error_not_a_crash()
    {
        using var places = new Places();

        Assert.StartsWith("There is no theme folder at ", places.Error("bad\0name/x"));
    }

    [Fact]
    public void RelativeTo_moves_only_the_folder_that_relative_paths_and_the_last_lookup_start_from()
    {
        using var places = new Places();
        using var elsewhere = new TempDir();
        var path = elsewhere.Folder(@"themes\mine");
        var name = elsewhere.Folder("loose");
        var user = places.Theme(places.User, "neon");
        places.Theme(places.Current, "loose");

        var moved = places.Locator.RelativeTo(elsewhere.Path);

        Assert.Equal((elsewhere.Path, places.Locator.ThemeFolders), (moved.CurrentDirectory, moved.ThemeFolders));
        Assert.True(moved.TryLocate(@"themes\mine", out var found, out _));
        Assert.Equal(path, found);
        Assert.True(moved.TryLocate("loose", out found, out _));
        Assert.Equal(name, found);
        Assert.True(moved.TryLocate("neon", out found, out _));
        Assert.Equal(user, found);
    }

    [Fact]
    public void The_default_looks_in_the_users_themes_then_beside_the_exe()
    {
        var locator = ThemeLocator.Default;

        Assert.Equal([Core.MarqueePaths.Themes, Path.Combine(AppContext.BaseDirectory, "themes")], locator.ThemeFolders);
    }
}
