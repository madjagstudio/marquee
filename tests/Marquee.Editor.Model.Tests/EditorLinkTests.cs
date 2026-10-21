using Marquee.Core.Files;
using Marquee.Core.Themes;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// The links the editor won't save through, made for real: a junction on a theme's path opens read-only and refuses a
/// save, and so does a symbolic link in place of layout.yaml, which a save would otherwise replace with a plain file.
/// (OneDrive's placeholders, which it does save through, are faked in ReparseTagsTests.)
public class EditorLinkTests
{
    [Fact]
    public void A_junction_is_read_as_a_mount_point_not_a_placeholder()
    {
        using var folder = new TempFolder();
        using var target = new TempFolder();
        var link = folder.Combine("link");
        Links.MakeJunction(link, target.Path);
        try
        {
            Assert.Equal(0xA0000003U, ReparseTags.Windows.Tag(link));
            Assert.Equal(link, ReparseTags.Windows.FirstLink(Path.Combine(link, ThemeLoader.LayoutFileName)));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void A_theme_reached_through_a_junction_opens_read_only_and_refuses_a_save()
    {
        using var data = new TempFolder();
        using var theme = TempTheme.With(Saving.Layout);
        var link = data.Combine("linked-theme");
        Links.MakeJunction(link, theme.Folder);
        try
        {
            var access = new ThemeOpening(data.Combine("installed"), data.Combine("themes")).Check(link);
            Assert.True(access.IsReadOnly);
            Assert.Contains("junction", access.Reason);

            var layout = Path.Combine(link, ThemeLoader.LayoutFileName);
            var file = new LayoutFile(layout, File.ReadAllBytes(layout));
            Assert.Throws<UnsafePathException>(() => file.Write(System.Text.Encoding.UTF8.GetBytes(Saving.Moved(20)), file.Taken));
            Assert.Equal(Saving.Layout, theme.Text);
            Assert.Empty(Directory.EnumerateFiles(theme.Folder, "*.tmp"));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void A_symbolic_link_in_place_of_layout_yaml_opens_read_only_and_is_kept_as_it_is()
    {
        using var theme = TempTheme.With(Saving.Layout);
        using var elsewhere = new TempFolder();
        var target = elsewhere.Combine(ThemeLoader.LayoutFileName);
        File.Move(theme.LayoutPath, target);
        if (!Links.TryMakeFileSymbolicLink(theme.LayoutPath, target))
        {
            File.Move(target, theme.LayoutPath);
            return; // Windows makes symbolic links only for administrators or with developer mode on.
        }

        var access = new ThemeOpening(elsewhere.Combine("installed"), elsewhere.Combine("themes")).Check(theme.Folder);
        Assert.True(access.IsReadOnly);

        var file = new LayoutFile(theme.LayoutPath, File.ReadAllBytes(theme.LayoutPath));
        Assert.Throws<UnsafePathException>(() => file.Write(System.Text.Encoding.UTF8.GetBytes(Saving.Moved(20)), file.Taken));
        Assert.True(File.GetAttributes(theme.LayoutPath).HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(Saving.Layout, File.ReadAllText(target));
    }
}
