using System.Text;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// LayoutFile.Write never deletes a hand edit that lands as it replaces the file, and a replace that fails part
/// way leaves layout.yaml where it was.
public class LayoutFileTests
{
    static readonly byte[] Opened = Encoding.UTF8.GetBytes("widgets: []\n");
    static readonly byte[] Saved = Encoding.UTF8.GetBytes("widgets: [] # saved\n");
    static readonly byte[] First = Encoding.UTF8.GetBytes("widgets: [] # first hand edit\n");

    sealed class Setup : IDisposable
    {
        public TempFolder Folder { get; } = new();

        public Setup()
        {
            Path = Folder.Combine("layout.yaml");
            File.WriteAllBytes(Path, Opened);
            Layout = new LayoutFile(Path, Opened);
        }

        public string Path { get; }

        public LayoutFile Layout { get; }

        /// Every file in the folder but layout.yaml: hand edits kept, or hidden files left behind.
        public IReadOnlyList<string> Others() =>
            Directory.EnumerateFiles(Folder.Path).Select(System.IO.Path.GetFileName).Where(name => name != "layout.yaml").ToList()!;

        public void Dispose() => Folder.Dispose();
    }

    [Fact]
    public void A_hand_edit_put_back_with_nothing_landing_after_it_leaves_no_file_behind()
    {
        using var setup = new Setup();
        setup.Layout.BeforeReplace = () => File.WriteAllBytes(setup.Path, First);

        Assert.Equal(LayoutWrite.ChangedOutside, setup.Layout.Write(Saved, setup.Layout.Taken));

        Assert.Equal(First, File.ReadAllBytes(setup.Path));
        Assert.Empty(setup.Others());
    }

    [Fact]
    public void A_replace_that_fails_part_way_puts_layout_yaml_back()
    {
        // The QA re-review's second defect: File.Replace failing with ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 has moved the
        // file to the backup name, which Write's clean-up used to delete.
        using var setup = new Setup();
        setup.Layout.Replace = (temporary, path, displaced) =>
        {
            File.Move(path, displaced);
            throw new IOException("Unable to move the replacement file to the file to be replaced.", unchecked((int)0x80070499));
        };

        Assert.Throws<IOException>(() => setup.Layout.Write(Saved, setup.Layout.Taken));

        Assert.Equal(Opened, File.ReadAllBytes(setup.Path));
        Assert.Empty(setup.Others());
        // The file is as the session left it, so the next save goes ahead.
        setup.Layout.Replace = AtomicFile.TryReplace;
        Assert.Equal(LayoutWrite.Written, setup.Layout.Write(Saved, setup.Layout.Taken));
        Assert.Equal(Saved, File.ReadAllBytes(setup.Path));
    }
}
