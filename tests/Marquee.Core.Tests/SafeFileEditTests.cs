using System.Runtime.InteropServices;
using Marquee.Core.Files;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// SafeFile.Edit, which reads a file and replaces it under one held folder, and SafeFile.CreateDirectory. Elevated, a
/// read through a link would hand an administrator's file to the user once the edit is written back where they can
/// read it, so the read is guarded as the write is.
public class SafeFileEditTests
{
    [Fact]
    public void Edit_replaces_the_file_with_what_the_edit_returns_leaving_no_temporary_file()
    {
        using var folder = new TempFolder();
        var path = folder.File("config.yaml");
        File.WriteAllText(path, "theme: one\n");

        SafeFile.Edit(path, text => text.Replace("one", "two", StringComparison.Ordinal));

        Assert.Equal("theme: two\n", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(folder.Path));
    }

    [Fact]
    public void Edit_reads_a_missing_file_as_empty_and_makes_its_folders()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "Marquee", "config.yaml");
        string? read = null;

        SafeFile.Edit(path, text => (read = text) + "theme: neon\n");

        Assert.Equal("", read);
        Assert.Equal("theme: neon\n", File.ReadAllText(path));
    }

    [Fact]
    public void Edit_reads_a_byte_order_mark_as_File_ReadAllText_does()
    {
        using var folder = new TempFolder();
        var path = folder.File("config.yaml");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. "theme: é\n"u8]);
        string? read = null;

        SafeFile.Edit(path, text => read = text);

        Assert.Equal("theme: é\n", read);
    }

    /// Held open without delete sharing (as an editor might), the file can be read but not replaced: an edit that
    /// changes nothing must not try.
    [Fact]
    public void Edit_writes_nothing_when_the_text_is_unchanged()
    {
        using var folder = new TempFolder();
        var path = folder.File("config.yaml");
        File.WriteAllText(path, "theme: neon\n");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            SafeFile.Edit(path, text => text);

        Assert.Equal("theme: neon\n", File.ReadAllText(path));
    }

    [Fact]
    public void Edit_refuses_a_junction_on_the_path_and_never_reads_behind_it()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        File.WriteAllText(elsewhere.File("config.yaml"), "secret: 1\n");
        var link = folder.File("Marquee");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            var edited = false;

            var ex = Assert.Throws<UnsafePathException>(() => SafeFile.Edit(Path.Combine(link, "config.yaml"), text =>
            {
                edited = true;
                return text + "theme: neon\n";
            }));

            Assert.Contains(link, ex.Message);
            Assert.False(edited);
            Assert.Equal("secret: 1\n", File.ReadAllText(elsewhere.File("config.yaml")));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void Edit_refuses_a_symbolic_link_in_place_of_the_file_and_never_reads_its_target()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var target = elsewhere.File("secret.txt");
        File.WriteAllText(target, "secret: 1\n");
        var path = folder.File("config.yaml");
        if (!Links.TryMakeFileSymbolicLink(path, target)) return;
        var edited = false;

        Assert.Throws<UnsafePathException>(() => SafeFile.Edit(path, text => { edited = true; return text; }));

        Assert.False(edited);
        Assert.Equal("secret: 1\n", File.ReadAllText(target));
    }

    /// The check before reading can miss a link made a moment later; the file is then opened without following a
    /// reparse point and refused if it is one. A symbolic link that exists when the file is opened stands in for one
    /// made in that moment.
    [Fact]
    public void A_file_that_is_a_symbolic_link_is_refused_when_it_is_opened()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var target = elsewhere.File("secret.txt");
        File.WriteAllText(target, "secret: 1\n");
        var path = folder.File("config.yaml");
        if (!Links.TryMakeFileSymbolicLink(path, target)) return;

        var ex = Assert.Throws<UnsafePathException>(() => SafeFile.ReadUnlinked(path));

        Assert.Contains(path, ex.Message);
    }

    /// The same for a junction in place of the file, which needs no special rights to make.
    [Fact]
    public void A_junction_in_place_of_the_file_is_refused_when_it_is_opened()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var path = folder.File("config.yaml");
        Links.MakeJunction(path, elsewhere.Path);
        try
        {
            Assert.Throws<UnsafePathException>(() => SafeFile.ReadUnlinked(path));
        }
        finally
        {
            Links.RemoveJunction(path);
        }
    }

    /// A hard link is another name for the same file, which may be one only an administrator can read. It is no
    /// reparse point, so it is refused by its count of names.
    [Fact]
    public void Edit_refuses_a_file_with_another_hard_link()
    {
        using var folder = new TempFolder();
        var target = folder.File("secret.txt");
        File.WriteAllText(target, "secret: 1\n");
        var path = folder.File("config.yaml");
        if (!CreateHardLinkW(path, target, IntPtr.Zero)) return; // a file system without hard links
        var edited = false;

        var ex = Assert.Throws<UnsafePathException>(() => SafeFile.Edit(path, text => { edited = true; return text + "x"; }));

        Assert.Contains(path, ex.Message);
        Assert.False(edited);
        Assert.Equal("secret: 1\n", File.ReadAllText(target));
    }

    [Fact]
    public void CreateDirectory_makes_the_folder_and_the_missing_ones_above_it()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "Marquee", "themes");

        SafeFile.CreateDirectory(path);
        SafeFile.CreateDirectory(path + Path.DirectorySeparatorChar); // already there, named with a trailing backslash

        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void CreateDirectory_refuses_a_junction_on_the_path_and_makes_nothing_behind_it()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var link = folder.File("Marquee");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            var ex = Assert.Throws<UnsafePathException>(() => SafeFile.CreateDirectory(Path.Combine(link, "themes")));

            Assert.Contains(link, ex.Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere.Path));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void CreateDirectory_refuses_a_junction_in_place_of_the_folder()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var link = folder.File("themes");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            Assert.Throws<UnsafePathException>(() => SafeFile.CreateDirectory(link));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);
}
