using System.Security.AccessControl;
using System.Security.Principal;
using Marquee.Core.Themes;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Before a theme opens: whether the editor may save it (an installed example, or a folder the user can't write, is
/// opened read-only with an offer to make a copy), the copy's free name, and one editor per theme folder.
public class ThemeOpeningTests
{
    sealed class Folders : IDisposable
    {
        public TempFolder Install { get; } = new();

        public TempFolder Data { get; } = new();

        public string Installed => Install.Combine("themes");

        public string UserThemes => Data.Combine("themes");

        public ThemeOpening Opening => new(Installed, UserThemes);

        /// A theme folder named `name` under `parent`, with a layout and an image.
        public static string Theme(string parent, string name)
        {
            var folder = Path.Combine(parent, name);
            Directory.CreateDirectory(Path.Combine(folder, "images"));
            File.WriteAllText(Path.Combine(folder, ThemeLoader.LayoutFileName), Saving.Layout);
            File.WriteAllBytes(Path.Combine(folder, "images", "art.png"), [1, 2, 3]);
            return folder;
        }

        public void Dispose()
        {
            Install.Dispose();
            Data.Dispose();
        }
    }

    [Fact]
    public void An_installed_example_is_read_only_and_says_why()
    {
        using var folders = new Folders();
        var example = Folders.Theme(folders.Installed, "example-showcase");

        var access = folders.Opening.Check(example);

        Assert.True(access.IsReadOnly);
        Assert.Contains("installed with Marquee", access.Reason);
    }

    [Fact]
    public void A_users_own_theme_can_be_edited_and_the_check_leaves_nothing_behind()
    {
        using var folders = new Folders();
        var theme = Folders.Theme(folders.UserThemes, "mine");
        var before = Directory.GetFileSystemEntries(theme);

        var access = folders.Opening.Check(theme);

        Assert.False(access.IsReadOnly);
        Assert.Null(access.Reason);
        Assert.Equal(before, Directory.GetFileSystemEntries(theme));
    }

    [Fact]
    public void A_folder_whose_name_only_starts_like_the_install_folder_is_not_an_example()
    {
        using var folders = new Folders();
        var theme = Folders.Theme(folders.Install.Combine("themes-mine"), "mine");

        Assert.False(folders.Opening.Check(theme).IsReadOnly);
    }

    [Fact]
    public void A_folder_the_user_cannot_write_in_is_read_only_and_says_why()
    {
        using var folders = new Folders();
        var theme = Folders.Theme(folders.Data.Path, "locked");
        var info = new DirectoryInfo(theme);
        var security = info.GetAccessControl();
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            var access = folders.Opening.Check(theme);

            Assert.True(access.IsReadOnly);
            Assert.Contains("can't save", access.Reason);
        }
        finally
        {
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public void A_read_only_layout_is_read_only()
    {
        using var folders = new Folders();
        var theme = Folders.Theme(folders.UserThemes, "mine");
        File.SetAttributes(Path.Combine(theme, ThemeLoader.LayoutFileName), FileAttributes.ReadOnly);

        var access = folders.Opening.Check(theme);

        Assert.True(access.IsReadOnly);
        Assert.Contains(ThemeLoader.LayoutFileName, access.Reason);
    }

    [Fact]
    public void A_copy_takes_the_first_free_name_in_the_users_themes()
    {
        using var folders = new Folders();
        var example = Folders.Theme(folders.Installed, "example-showcase");

        Assert.Equal(Path.Combine(folders.UserThemes, "example-showcase-copy"), folders.Opening.CopyFolderFor(example));
        var first = folders.Opening.MakeCopy(example);
        var second = folders.Opening.MakeCopy(example);
        var ofTheCopy = folders.Opening.MakeCopy(second);

        Assert.Equal(Path.Combine(folders.UserThemes, "example-showcase-copy"), first);
        Assert.Equal(Path.Combine(folders.UserThemes, "example-showcase-copy-2"), second);
        Assert.Equal(Path.Combine(folders.UserThemes, "example-showcase-copy-3"), ofTheCopy);
    }

    [Fact]
    public void A_copy_has_every_file_and_can_be_edited()
    {
        using var folders = new Folders();
        var example = Folders.Theme(folders.Installed, "example-showcase");
        foreach (var file in Directory.EnumerateFiles(example, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.ReadOnly);

        var copy = folders.Opening.MakeCopy(example);

        Assert.Equal(Saving.Layout, File.ReadAllText(Path.Combine(copy, ThemeLoader.LayoutFileName)));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(copy, "images", "art.png")));
        Assert.False(folders.Opening.Check(copy).IsReadOnly);
        Assert.Single(Directory.GetDirectories(folders.UserThemes));
    }

    [Fact]
    public void An_example_copied_from_the_repository_opens()
    {
        using var folders = new Folders();
        using var example = TempTheme.Example("example-showcase");
        var opening = new ThemeOpening(Path.GetDirectoryName(example.Folder)!, folders.UserThemes);
        Assert.True(opening.Check(example.Folder).IsReadOnly);

        var copy = opening.MakeCopy(example.Folder);

        Assert.Equal(Path.Combine(folders.UserThemes, Path.GetFileName(example.Folder) + "-copy"), copy);
        using var session = EditorSession.Open(copy);
        Assert.False(session.IsPaused);
    }

    [Fact]
    public void One_editor_holds_a_theme_folder_however_its_path_is_written()
    {
        using var theme = TempTheme.With(Saving.Layout);

        using (var held = ThemeOpening.TryLock(theme.Folder))
        {
            Assert.NotNull(held);
            Assert.Null(ThemeOpening.TryLock(theme.Folder.ToUpperInvariant() + Path.DirectorySeparatorChar));
            Assert.Null(ThemeOpening.TryLock(Path.Combine(theme.Folder, "images", "..")));
        }

        using var again = ThemeOpening.TryLock(theme.Folder);
        Assert.NotNull(again);
    }

    [Fact]
    public void Different_theme_folders_are_held_apart()
    {
        using var one = TempTheme.With(Saving.Layout);
        using var two = TempTheme.With(Saving.Layout);

        using var first = ThemeOpening.TryLock(one.Folder);
        using var second = ThemeOpening.TryLock(two.Folder);

        Assert.NotNull(first);
        Assert.NotNull(second);
    }
}
