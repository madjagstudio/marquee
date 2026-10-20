using Marquee.Core.Themes;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Where an import may write, by the rule saves follow (ReparseTags): through OneDrive's placeholders, never through a
/// junction, a symbolic link or a mount point, and never into a theme folder that is gone. The tags are faked here; the
/// real junction refusal is in AssetImportTests.
public class AssetImportFolderTests
{
    [Fact]
    public void A_theme_in_a_onedrive_folder_takes_an_import_in_place()
    {
        using var theme = TempTheme.With(Saving.Layout);
        using var downloads = new TempFolder();
        var source = Assets.Png(downloads.Combine("logo.png"));
        var tags = new FakeReparseTags().With(theme.Folder, FakeReparseTags.OneDrive);

        var imported = new AssetImport(theme.Folder, Assets.Checks, AssetImport.MaxBytes, tags).Import(source, AssetKind.Image);

        Assert.Equal(new ImportedFile("images/logo.png", AssetKind.Image, null, Copied: true), imported);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(theme.Folder, "images", "logo.png")));
        Assert.Empty(Directory.EnumerateFiles(theme.Folder, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_file_already_in_a_onedrive_theme_is_used_where_it_is()
    {
        using var theme = TempTheme.With(Saving.Layout);
        Directory.CreateDirectory(Path.Combine(theme.Folder, "art"));
        var inside = Assets.Png(Path.Combine(theme.Folder, "art", "logo.png"));
        var tags = new FakeReparseTags()
            .With(theme.Folder, FakeReparseTags.OneDrive)
            .With(Path.Combine(theme.Folder, "art"), FakeReparseTags.OneDrive)
            .With(inside, FakeReparseTags.OneDrive);

        var imported = new AssetImport(theme.Folder, Assets.Checks, AssetImport.MaxBytes, tags).Import(inside, AssetKind.Image);

        Assert.Equal(new ImportedFile("art/logo.png", AssetKind.Image, null, Copied: false), imported);
        Assert.False(Directory.Exists(Path.Combine(theme.Folder, "images")));
    }

    [Fact]
    public void A_link_tag_on_the_theme_folder_is_refused_and_nothing_is_written()
    {
        using var theme = TempTheme.With(Saving.Layout);
        using var downloads = new TempFolder();
        var source = Assets.Png(downloads.Combine("logo.png"));
        var tags = new FakeReparseTags().With(theme.Folder, FakeReparseTags.SymbolicLink);

        var ex = Assert.Throws<AssetImportException>(
            () => new AssetImport(theme.Folder, Assets.Checks, AssetImport.MaxBytes, tags).Import(source, AssetKind.Image));

        Assert.Contains("junction", ex.Reason);
        Assert.False(Directory.Exists(Path.Combine(theme.Folder, "images")));
    }

    [Fact]
    public void An_import_into_a_theme_folder_that_is_gone_does_not_make_it_again()
    {
        var theme = TempTheme.With(Saving.Layout);
        using var downloads = new TempFolder();
        var source = Assets.Png(downloads.Combine("logo.png"));
        var import = new AssetImport(theme.Folder, Assets.Checks);
        theme.Dispose();

        var ex = Assert.Throws<AssetImportException>(() => import.Import(source, AssetKind.Image));

        Assert.Contains("folder is gone", ex.Reason);
        Assert.False(Directory.Exists(theme.Folder));
    }

    [Fact]
    public void A_copy_the_edit_does_not_take_is_deleted_from_a_onedrive_theme()
    {
        using var theme = TempTheme.With(Saving.Layout);
        using var downloads = new TempFolder();
        using var session = theme.Open();
        session.Apply(new ReplaceDocument(Saving.Layout.Replace("shape: rect", "shape: hexagon"), "Changed outside the editor"));
        var source = Assets.Png(downloads.Combine("logo.png"));
        var tags = new FakeReparseTags().With(theme.Folder, FakeReparseTags.OneDrive);

        // Paused on a theme error, the edit is refused, and the copy made for it goes.
        Assert.Throws<InvalidOperationException>(
            () => new AssetImport(theme.Folder, Assets.Checks, AssetImport.MaxBytes, tags).AddImage(session, source));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(theme.Folder, "images")));
    }
}
