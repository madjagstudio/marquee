using Marquee.App.Tests.TestSupport;

namespace Marquee.App.Tests;

public sealed class ThemeCatalogTests : IDisposable
{
    readonly TempDir _dir = new();

    string User => _dir.File("user");

    string Installed => _dir.File("installed");

    void Theme(string folder, string name) => _dir.Write(Path.Combine(folder, name, "layout.yaml"), "canvas: {}\n");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Each_folder_lists_its_themes_by_name_in_the_order_the_folders_come()
    {
        Theme("user", "zebra");
        Theme("user", "Aurora");
        Theme("installed", "example-portrait");
        Theme("installed", "example-landscape");

        var groups = ThemeCatalog.List([User, Installed]);

        Assert.Equal(2, groups.Count);
        Assert.Equal(["Aurora", "zebra"], groups[0].Select(theme => theme.Name));
        Assert.Equal(["example-landscape", "example-portrait"], groups[1].Select(theme => theme.Name));
        Assert.Equal(Path.Combine(User, "Aurora"), groups[0][0].Folder);
    }

    [Fact]
    public void A_name_in_an_earlier_folder_hides_the_same_name_later_as_the_theme_lookup_does()
    {
        Theme("user", "example-landscape");
        Theme("installed", "Example-Landscape");
        Theme("installed", "example-portrait");

        var groups = ThemeCatalog.List([User, Installed]);

        Assert.Equal(Path.Combine(User, "example-landscape"), Assert.Single(groups[0]).Folder);
        Assert.Equal(["example-portrait"], groups[1].Select(theme => theme.Name));
    }

    [Fact]
    public void Only_folders_with_a_layout_are_themes_and_missing_folders_are_empty()
    {
        Theme("user", "real");
        _dir.Folder(Path.Combine("user", "assets-only"));
        _dir.Write(Path.Combine("user", "notes.txt"), "");

        var groups = ThemeCatalog.List([User, _dir.File("nowhere")]);

        Assert.Equal(["real"], groups[0].Select(theme => theme.Name));
        Assert.Empty(groups[1]);
    }
}
