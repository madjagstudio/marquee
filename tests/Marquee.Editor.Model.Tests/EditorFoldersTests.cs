using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Where the editor keeps a theme's opened version and recovery text: a readable name with a short hash of the folder's
/// full path, since themes in different folders can share a name.
public class EditorFoldersTests
{
    [Fact]
    public void A_themes_files_are_named_for_it_and_its_folder()
    {
        var folders = new EditorFolders(@"C:\data\editor");

        var opened = folders.OpenedFile(@"C:\themes\example-showcase");
        var recovery = folders.RecoveryFile(@"C:\themes\example-showcase");

        Assert.Matches(@"^C:\\data\\editor\\opened\\example-showcase-[0-9a-f]{8}\.yaml$", opened);
        Assert.Equal(Path.Combine(folders.Recovery, Path.GetFileName(opened)), recovery);
    }

    [Fact]
    public void Themes_with_one_name_in_different_folders_get_different_files()
    {
        var folders = new EditorFolders(@"C:\data\editor");

        Assert.NotEqual(folders.RecoveryFile(@"C:\themes\mine"), folders.RecoveryFile(@"D:\backup\mine"));
    }

    [Theory]
    [InlineData(@"C:\Themes\Mine\")]
    [InlineData(@"c:\themes\mine")]
    [InlineData(@"C:\themes\other\..\mine")]
    public void One_folder_written_differently_gets_one_file(string spelling)
    {
        var folders = new EditorFolders(@"C:\data\editor");

        Assert.Equal(
            Path.GetFileName(folders.RecoveryFile(@"C:\themes\mine")).ToLowerInvariant(),
            Path.GetFileName(folders.RecoveryFile(spelling)).ToLowerInvariant());
    }

    [Fact]
    public void A_short_name_is_the_same_folder()
    {
        using var data = new TempFolder();
        var longName = data.Combine("a theme folder with a long name");
        Directory.CreateDirectory(longName);

        var shortName = ThemeIdentity.ShortPath(longName);
        if (shortName is null || shortName == longName) return; // 8.3 names are off on this volume

        Assert.Equal(ThemeIdentity.Key(longName), ThemeIdentity.Key(shortName));
        Assert.Equal(ThemeIdentity.MutexName(longName), ThemeIdentity.MutexName(shortName));
    }

    [Fact]
    public void Characters_a_file_name_cannot_hold_are_replaced()
    {
        Assert.Matches("^theme-[0-9a-f]{8}$", ThemeIdentity.Key(@"C:\"));
        Assert.Matches("^my-theme-[0-9a-f]{8}$", ThemeIdentity.Key(@"C:\themes\my theme"));
    }
}
