using System.Runtime.InteropServices;
using Marquee.Core.Files;
using static Marquee.Editor.Model.EditorNative;

namespace Marquee.Editor.Model;

/// Which reparse points on a theme's path the editor saves through. SafeFile refuses every one, for the elevated tray,
/// where a link the user planted is a way round UAC; the editor runs unelevated, so that reason doesn't apply. What
/// still matters is that a save lands where the path says:
/// - a cloud files placeholder (IO_REPARSE_TAG_CLOUD and its variants, which OneDrive puts on every synced file and
///   folder) keeps the file where its path says, so it is allowed: themes kept in Documents or on the Desktop, which
///   are OneDrive folders on many PCs, are edited in place;
/// - a junction, a symbolic link, a mount point or any other kind may send the save somewhere else, or would be
///   replaced by a plain file when a save renames over it, so it is refused.
///
/// The tag is read through FILE_ATTRIBUTE_TAG_INFO on a handle opened for attributes only, without following the
/// reparse point, which doesn't download a placeholder. Tag is virtual so tests can fake a placeholder without
/// OneDrive.
internal class ReparseTags
{
    public static readonly ReparseTags Windows = new();

    /// IO_REPARSE_TAG_CLOUD; IO_REPARSE_TAG_CLOUD_1 to _F set bits 12 to 15 on it.
    const uint CloudTag = 0x9000001A;
    const uint CloudMask = 0xFFFF0FFF;

    /// Whether `tag` is a cloud files placeholder's.
    public static bool IsCloud(uint tag) => (tag & CloudMask) == CloudTag;

    /// The reparse tag of the entry at `path` itself, not of what it points to; null when it isn't a reparse point, and
    /// 0, which counts as a link, when it is one whose tag can't be read. Throws FileNotFoundException or
    /// DirectoryNotFoundException when nothing is there, and IOException or UnauthorizedAccessException when its
    /// attributes can't be read.
    public virtual uint? Tag(string path)
    {
        if (!File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return null;
        using var handle = CreateFile(path, FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid) return 0;
        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out var info, Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>()))
            return 0;
        return ((FileAttributes)info.FileAttributes).HasFlag(FileAttributes.ReparsePoint) ? info.ReparseTag : null;
    }

    /// The first part of `path`, from the root down and the last part included, that is a reparse point other than a
    /// cloud placeholder; null when none is. A part that doesn't exist, or can't be looked at, ends the search: a write
    /// there fails on its own.
    public string? FirstLink(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var at = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            at = Path.Combine(at, part);
            uint? tag;
            try
            {
                tag = Tag(at);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            if (tag is { } found && !IsCloud(found)) return at;
        }

        return null;
    }

    /// The refusal for a save, an import or a delete through `link`.
    public static UnsafePathException Refusal(string target, string link) =>
        new($"The editor won't write {target}: {link} is a junction, a symbolic link or a mount point, which could send the write somewhere else.");
}
