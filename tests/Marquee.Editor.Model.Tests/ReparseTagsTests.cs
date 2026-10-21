using Marquee.Core.Files;
using Marquee.Core.Themes;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Themes in OneDrive folders are edited in place: a cloud placeholder on the path is allowed, any other reparse point
/// (a junction, a symbolic link, a mount point) is not, when opening and when saving. The tags are faked here; the real
/// junction and symbolic link refusals are in EditorLinkTests.
public class ReparseTagsTests
{
    [Theory]
    [InlineData(0x9000001AU, true)] // IO_REPARSE_TAG_CLOUD
    [InlineData(0x9000101AU, true)] // IO_REPARSE_TAG_CLOUD_1
    [InlineData(0x9000601AU, true)] // IO_REPARSE_TAG_CLOUD_6, OneDrive's
    [InlineData(0x9000F01AU, true)] // IO_REPARSE_TAG_CLOUD_F
    [InlineData(0xA0000003U, false)] // IO_REPARSE_TAG_MOUNT_POINT: a junction or a mount point
    [InlineData(0xA000000CU, false)] // IO_REPARSE_TAG_SYMLINK
    [InlineData(0x9000001BU, false)]
    [InlineData(0x0000001AU, false)]
    public void Only_cloud_placeholder_tags_are_cloud(uint tag, bool cloud) => Assert.Equal(cloud, ReparseTags.IsCloud(tag));

    [Fact]
    public void A_plain_folder_has_no_tag_and_a_missing_one_throws()
    {
        using var folder = new TempFolder();

        Assert.Null(ReparseTags.Windows.Tag(folder.Path));
        Assert.ThrowsAny<IOException>(() => ReparseTags.Windows.Tag(folder.Combine("missing")));
        Assert.Null(ReparseTags.Windows.FirstLink(folder.Combine("missing", "layout.yaml")));
    }

    [Fact]
    public void Placeholders_on_the_path_are_not_links_and_a_link_tag_anywhere_is()
    {
        using var folder = new TempFolder();
        var theme = Directory.CreateDirectory(folder.Combine("OneDrive", "Documents", "theme")).FullName;
        var layout = Path.Combine(theme, ThemeLoader.LayoutFileName);
        File.WriteAllText(layout, Saving.Layout);
        var tags = new FakeReparseTags()
            .With(folder.Combine("OneDrive"), FakeReparseTags.OneDrive)
            .With(folder.Combine("OneDrive", "Documents"), FakeReparseTags.OneDrive)
            .With(layout, FakeReparseTags.OneDrive);

        Assert.Null(tags.FirstLink(layout));

        tags.With(folder.Combine("OneDrive", "Documents"), FakeReparseTags.SymbolicLink);
        Assert.Equal(folder.Combine("OneDrive", "Documents"), tags.FirstLink(layout));
    }

    [Fact]
    public void A_theme_in_a_onedrive_folder_can_be_edited_in_place()
    {
        using var install = new TempFolder();
        using var data = new TempFolder();
        var theme = Directory.CreateDirectory(data.Combine("OneDrive", "Documents", "mine")).FullName;
        var layout = Path.Combine(theme, ThemeLoader.LayoutFileName);
        File.WriteAllText(layout, Saving.Layout);
        var tags = new FakeReparseTags()
            .With(data.Combine("OneDrive"), FakeReparseTags.OneDrive)
            .With(data.Combine("OneDrive", "Documents"), FakeReparseTags.OneDrive)
            .With(theme, FakeReparseTags.OneDrive)
            .With(layout, FakeReparseTags.OneDrive);

        var access = new ThemeOpening(install.Combine("themes"), data.Combine("themes"), tags).Check(theme);

        Assert.Equal(ThemeAccess.Editable, access);
    }

    [Fact]
    public void A_theme_with_a_link_on_its_path_opens_read_only_and_says_where()
    {
        using var install = new TempFolder();
        using var data = new TempFolder();
        var theme = Directory.CreateDirectory(data.Combine("linked", "mine")).FullName;
        File.WriteAllText(Path.Combine(theme, ThemeLoader.LayoutFileName), Saving.Layout);
        var tags = new FakeReparseTags().With(data.Combine("linked"), FakeReparseTags.SymbolicLink);

        var access = new ThemeOpening(install.Combine("themes"), data.Combine("themes"), tags).Check(theme);

        Assert.True(access.IsReadOnly);
        Assert.Contains(data.Combine("linked"), access.Reason);
        Assert.Contains("junction", access.Reason);
    }

    [Fact]
    public void A_save_through_onedrive_folders_is_written_in_place()
    {
        using var data = new TempFolder();
        var theme = Directory.CreateDirectory(data.Combine("OneDrive", "mine")).FullName;
        var layout = Path.Combine(theme, ThemeLoader.LayoutFileName);
        File.WriteAllText(layout, Saving.Layout);
        var tags = new FakeReparseTags()
            .With(data.Combine("OneDrive"), FakeReparseTags.OneDrive)
            .With(theme, FakeReparseTags.OneDrive)
            .With(layout, FakeReparseTags.OneDrive);
        var file = new LayoutFile(layout, File.ReadAllBytes(layout), tags);

        file.Write(System.Text.Encoding.UTF8.GetBytes(Saving.Moved(20)), file.Taken);

        Assert.Equal(Saving.Moved(20), File.ReadAllText(layout));
        Assert.Equal([ThemeLoader.LayoutFileName], Directory.GetFiles(theme).Select(Path.GetFileName));
    }

    [Fact]
    public void A_save_with_a_link_tag_on_the_file_is_refused_and_the_file_kept()
    {
        using var theme = TempTheme.With(Saving.Layout);
        var tags = new FakeReparseTags().With(theme.LayoutPath, FakeReparseTags.SymbolicLink);
        var file = new LayoutFile(theme.LayoutPath, File.ReadAllBytes(theme.LayoutPath), tags);

        var ex = Assert.Throws<UnsafePathException>(() => file.Write(System.Text.Encoding.UTF8.GetBytes(Saving.Moved(20)), file.Taken));

        Assert.Contains("symbolic link", ex.Message);
        Assert.Equal(Saving.Layout, theme.Text);
        Assert.Empty(Directory.EnumerateFiles(theme.Folder, "*.tmp"));
    }

    [Fact]
    public void A_save_keeps_what_windows_keeps_with_the_file()
    {
        // File.Replace keeps the old file's streams, permissions and OneDrive item; a rename over it would make a new
        // file. A stream of its own (as Explorer's mark of the web is) shows it.
        using var theme = TempTheme.With(Saving.Layout);
        File.WriteAllText(theme.LayoutPath + ":marquee-test", "kept");
        var file = new LayoutFile(theme.LayoutPath, File.ReadAllBytes(theme.LayoutPath));

        file.Write(System.Text.Encoding.UTF8.GetBytes(Saving.Moved(20)), file.Taken);

        Assert.Equal(Saving.Moved(20), theme.Text);
        Assert.Equal("kept", File.ReadAllText(theme.LayoutPath + ":marquee-test"));
    }
}
