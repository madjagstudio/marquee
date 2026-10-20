namespace Marquee.Editor.Model.Tests.TestSupport;

/// Reparse tags faked by path, so a OneDrive placeholder (or a link's tag) is tested without OneDrive and without
/// touching a real synced folder. Paths not faked are read from Windows.
sealed class FakeReparseTags : ReparseTags
{
    /// IO_REPARSE_TAG_CLOUD_6, the tag OneDrive puts on the files and folders it syncs.
    public const uint OneDrive = 0x9000601A;

    /// IO_REPARSE_TAG_SYMLINK.
    public const uint SymbolicLink = 0xA000000C;

    readonly Dictionary<string, uint> _tags = new(StringComparer.OrdinalIgnoreCase);

    /// Fakes `tag` on the entry at `path`, which must exist, as a placeholder or a link does.
    public FakeReparseTags With(string path, uint tag)
    {
        _tags[Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))] = tag;
        return this;
    }

    public override uint? Tag(string path) =>
        _tags.TryGetValue(Path.TrimEndingDirectorySeparator(path), out var tag) ? tag : base.Tag(path);
}
