using Marquee.Core.Files;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// SafeFile's copy under a new name and its delete, for the theme editor's import and clean-up: neither goes through a
/// junction, and the copy never replaces anything.
public class SafeFileCopyTests
{
    [Fact]
    public void Copies_to_a_new_name_making_the_folder_and_leaving_no_temporary_file()
    {
        using var folder = new TempFolder();
        var source = folder.File("logo.png");
        File.WriteAllBytes(source, [1, 2, 3]);
        var target = Path.Combine(folder.Path, "theme", "images", "logo.png");

        Assert.True(SafeFile.TryCopyNew(source, target));

        Assert.Equal([1, 2, 3], File.ReadAllBytes(target));
        Assert.Equal([target], Directory.GetFileSystemEntries(Path.GetDirectoryName(target)!));
    }

    [Fact]
    public void A_name_already_taken_by_a_file_or_folder_is_left_alone()
    {
        using var folder = new TempFolder();
        var source = folder.File("logo.png");
        File.WriteAllBytes(source, [1, 2, 3]);
        var file = folder.File("taken.png");
        File.WriteAllBytes(file, [9]);
        Directory.CreateDirectory(folder.File("folder.png"));

        Assert.False(SafeFile.TryCopyNew(source, file));
        Assert.False(SafeFile.TryCopyNew(source, folder.File("folder.png")));

        Assert.Equal([9], File.ReadAllBytes(file));
        Assert.Equal(3, Directory.GetFileSystemEntries(folder.Path).Length); // the three, and no temporary file
    }

    [Fact]
    public void A_copy_through_a_junction_is_refused_and_writes_nothing_behind_it()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var source = folder.File("logo.png");
        File.WriteAllBytes(source, [1, 2, 3]);
        var link = folder.File("images");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            var ex = Assert.Throws<UnsafePathException>(() => SafeFile.TryCopyNew(source, Path.Combine(link, "logo.png")));

            Assert.Contains(link, ex.Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere.Path));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void Deletes_a_file_and_does_nothing_for_one_already_gone()
    {
        using var folder = new TempFolder();
        var file = folder.File("old.png");
        File.WriteAllBytes(file, [1]);

        SafeFile.Delete(file);
        SafeFile.Delete(file);
        SafeFile.Delete(Path.Combine(folder.Path, "missing", "old.png"));

        Assert.False(File.Exists(file));
    }

    /// The theme editor's clean-up deletes a file the user ticked though it is read-only (a theme unzipped or copied
    /// from a read-only place often is), as Explorer does; anything else asking to delete is refused as Windows refuses.
    [Fact]
    public void A_read_only_file_is_deleted_only_when_asked_to_be()
    {
        using var folder = new TempFolder();
        var file = folder.File("old.png");
        File.WriteAllBytes(file, [1]);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => SafeFile.Delete(file));
            Assert.True(File.Exists(file));

            SafeFile.Delete(file, evenReadOnly: true);

            Assert.False(File.Exists(file));
        }
        finally
        {
            if (File.Exists(file)) File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public void A_delete_through_a_junction_is_refused_and_keeps_what_it_leads_to()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var far = Path.Combine(elsewhere.Path, "far.png");
        File.WriteAllBytes(far, [1]);
        var link = folder.File("images");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            Assert.Throws<UnsafePathException>(() => SafeFile.Delete(Path.Combine(link, "far.png")));

            Assert.True(File.Exists(far));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void A_symbolic_link_is_refused_rather_than_deleted_where_Windows_makes_one()
    {
        using var folder = new TempFolder();
        var target = folder.File("target.png");
        File.WriteAllBytes(target, [1]);
        var link = folder.File("link.png");
        if (!Links.TryMakeFileSymbolicLink(link, target)) return; // the junction tests cover this machine

        Assert.Throws<UnsafePathException>(() => SafeFile.Delete(link));

        Assert.True(File.Exists(target));
        Assert.True(File.Exists(link));
    }

    [Fact]
    public void A_folder_is_not_deleted()
    {
        using var folder = new TempFolder();
        var inside = folder.File("images");
        Directory.CreateDirectory(inside);

        Assert.Throws<IOException>(() => SafeFile.Delete(inside));

        Assert.True(Directory.Exists(inside));
    }
}
