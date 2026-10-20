using System.Runtime.InteropServices;
using System.Text;
using Marquee.Core.Files;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

public class SafeFileTests
{
    [Fact]
    public void Writes_a_new_file_and_replaces_an_existing_one_leaving_no_temporary_file()
    {
        using var folder = new TempFolder();
        var path = folder.File("config.yaml");

        SafeFile.WriteAllText(path, "theme: one\n");
        Assert.Equal("theme: one\n", File.ReadAllText(path));
        SafeFile.WriteAllText(path, "theme: two\n");

        Assert.Equal("theme: two\n", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(folder.Path));
    }

    [Fact]
    public void Writes_UTF8_without_a_byte_order_mark()
    {
        using var folder = new TempFolder();
        var path = folder.File("config.yaml");

        SafeFile.WriteAllText(path, "theme: é\n");

        Assert.Equal(Encoding.UTF8.GetBytes("theme: é\n"), File.ReadAllBytes(path));
    }

    [Fact]
    public void Makes_missing_folders()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "Marquee", "sub", "config.yaml");

        SafeFile.WriteAllText(path, "x");

        Assert.Equal("x", File.ReadAllText(path));
    }

    [Fact]
    public void Refuses_a_junction_in_the_folder_path_and_writes_nothing_behind_it()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var link = folder.File("Marquee");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            var ex = Assert.Throws<UnsafePathException>(() => SafeFile.WriteAllText(Path.Combine(link, "config.yaml"), "x"));

            Assert.Contains(link, ex.Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere.Path));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void Refuses_a_junction_further_up_and_makes_no_folder_behind_it()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var link = folder.File("Local");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            Assert.Throws<UnsafePathException>(() => SafeFile.WriteAllText(Path.Combine(link, "Marquee", "config.yaml"), "x"));

            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere.Path));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void Refuses_a_junction_in_place_of_the_file()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var path = folder.File("config.yaml");
        Links.MakeJunction(path, elsewhere.Path);
        try
        {
            var ex = Assert.Throws<UnsafePathException>(() => SafeFile.WriteAllText(path, "x"));

            Assert.Contains(path, ex.Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere.Path));
        }
        finally
        {
            Links.RemoveJunction(path);
        }
    }

    [Fact]
    public void Refuses_a_symbolic_link_in_place_of_the_file_and_leaves_its_target_alone()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var target = elsewhere.File("important.dll");
        File.WriteAllText(target, "original");
        var path = folder.File("config.yaml");
        if (!Links.TryMakeFileSymbolicLink(path, target)) return;

        Assert.Throws<UnsafePathException>(() => SafeFile.WriteAllText(path, "x"));

        Assert.Equal("original", File.ReadAllText(target));
        Assert.NotNull(new FileInfo(path).LinkTarget);
    }

    /// The check before writing can miss a junction made a moment later; the folder is then opened, held so it can't
    /// be renamed or replaced, and refused unless it really is where the path says. A junction that exists when the
    /// folder is opened stands in for one made in that moment.
    [Fact]
    public void A_folder_reached_through_a_link_is_refused_when_it_is_opened()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        Directory.CreateDirectory(Path.Combine(elsewhere.Path, "Marquee"));
        var link = folder.File("Local");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            var ex = Assert.Throws<UnsafePathException>(() => SafeFile.OpenFolder(Path.Combine(link, "Marquee")).Dispose());

            Assert.Contains(Path.Combine(link, "Marquee"), ex.Message);
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void A_folder_held_open_cant_be_renamed()
    {
        using var folder = new TempFolder();
        var held = Directory.CreateDirectory(folder.File("Marquee")).FullName;

        using (SafeFile.OpenFolder(held))
            Assert.ThrowsAny<IOException>(() => Directory.Move(held, folder.File("Moved")));

        Directory.Move(held, folder.File("Moved"));
    }

    /// GitHub's runners give %TEMP% as C:\Users\RUNNER~1\…: a short name is the same folder, not a link.
    [Fact]
    public void A_short_name_on_the_path_is_not_taken_for_a_link()
    {
        using var folder = new TempFolder();
        var longName = Directory.CreateDirectory(folder.File("a folder with a long name")).FullName;
        var shortName = ShortPath(longName);
        if (shortName is null || string.Equals(shortName, longName, StringComparison.OrdinalIgnoreCase)) return; // 8.3 names are off here

        SafeFile.WriteAllText(Path.Combine(shortName, "config.yaml"), "x");
        SafeFile.OpenFolder(shortName).Dispose(); // as given, not expanded by Path.GetFullPath first

        Assert.Equal("x", File.ReadAllText(Path.Combine(longName, "config.yaml")));
    }

    /// A reader always sees the whole old file or the whole new one: the new text goes to a temporary file that is then
    /// renamed over the old one.
    [Fact]
    public async Task A_reader_never_sees_part_of_a_file()
    {
        using var folder = new TempFolder();
        var path = folder.File("config.yaml");
        var a = new string('a', 256 * 1024);
        var b = new string('b', 256 * 1024);
        SafeFile.WriteAllText(path, a);

        var torn = new List<string>();
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                // Shared for deleting too, as a careful reader opens a file others replace.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var text = new StreamReader(stream).ReadToEnd();
                if (text != a && text != b) torn.Add($"{text.Length} chars");
            }
        });

        for (var i = 0; i < 100; i++) SafeFile.WriteAllText(path, i % 2 == 0 ? b : a);
        stop.Cancel();
        await reader.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Empty(torn);
        Assert.Equal(a, File.ReadAllText(path));
    }

    /// File.ReadAllText (which config.yaml's watcher uses) doesn't share the file for deleting, so while it reads, the
    /// file can't be replaced; the write waits for it.
    [Fact]
    public void A_write_waits_a_moment_for_a_reader_that_blocks_replacing()
    {
        using var folder = new TempFolder();
        var path = folder.File("config.yaml");
        SafeFile.WriteAllText(path, "old");
        using var opened = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            opened.Set();
            Thread.Sleep(100);
        });
        holder.Start();
        Assert.True(opened.Wait(TimeSpan.FromSeconds(30)));

        SafeFile.WriteAllText(path, "new");

        holder.Join();
        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(folder.Path));
    }

    static string? ShortPath(string path)
    {
        var buffer = new char[1024];
        var length = GetShortPathNameW(path, buffer, buffer.Length);
        return length is > 0 and < 1024 ? new string(buffer, 0, length) : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int GetShortPathNameW(string longPath, char[] shortPath, int bufferLength);
}
