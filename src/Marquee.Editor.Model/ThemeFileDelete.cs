using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static Marquee.Editor.Model.EditorNative;

namespace Marquee.Editor.Model;

/// The clean-up's delete: SafeFile.Delete's care, by the editor's rule for reparse points (ReparseTags) rather than
/// SafeFile's, which stays strict for the elevated tray. A cloud placeholder (a OneDrive folder or file) is deleted
/// through like any folder or file; a junction, a symbolic link or a mount point on the path, or the file itself being
/// one, is refused (UnsafePathException), since deleting through it could delete a file somewhere else.
///
/// The folder is held meanwhile, so it can't be swapped for a link, and the file is opened without following a reparse
/// point and checked again by its handle, so a link made after the path's check is refused, never followed. Nothing is
/// downloaded: neither handle reads the file's data.
internal static class ThemeFileDelete
{
    /// Deletes the file at `path`; nothing to do when it is gone. A folder isn't deleted (IOException). A read-only file
    /// is refused (UnauthorizedAccessException) unless `evenReadOnly`: then it goes, by the handle already checked,
    /// without its attribute being changed first, where Windows can (10 1809 and later, on NTFS), and is refused
    /// elsewhere. Throws UnsafePathException for a link, and the IOException or UnauthorizedAccessException of a failure.
    public static void Delete(string path, ReparseTags tags, bool evenReadOnly = false)
    {
        var full = Path.GetFullPath(path);
        if (tags.FirstLink(full) is { } link) throw ReparseTags.Refusal(full, link, "delete");
        var folder = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(folder)) return;

        // Listing access, and no FILE_SHARE_DELETE: while it is held the folder can't be renamed, moved or replaced.
        using var held = CreateFile(folder, FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (held.IsInvalid) throw Failure($"Can't open {folder}", Marshal.GetLastPInvokeError());
        if (Link(held) is { } folderTag && !ReparseTags.IsCloud(folderTag)) throw ReparseTags.Refusal(full, folder, "delete");

        // DELETE access, shared as careful readers share, and no backup semantics: a folder fails to open.
        using var handle = CreateFile(full, DELETE | FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND) return;
            if (Directory.Exists(full)) throw NotAFile(full);
            throw Failure($"Can't delete {full}", error);
        }

        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out var info, Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>()))
            throw Failure($"Can't read {full}'s attributes", Marshal.GetLastPInvokeError());
        var attributes = (FileAttributes)info.FileAttributes;
        if (attributes.HasFlag(FileAttributes.ReparsePoint) && !ReparseTags.IsCloud(info.ReparseTag))
            throw ReparseTags.Refusal(full, full, "delete");
        if (attributes.HasFlag(FileAttributes.Directory)) throw NotAFile(full);

        if (evenReadOnly && attributes.HasFlag(FileAttributes.ReadOnly))
        {
            var ignoring = new FILE_DISPOSITION_INFO_EX
            {
                Flags = FILE_DISPOSITION_FLAG_DELETE | FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE,
            };
            if (SetFileInformationByHandle(handle, FileDispositionInfoEx, ref ignoring, Marshal.SizeOf<FILE_DISPOSITION_INFO_EX>())) return;
            // An older Windows or another file system doesn't know the flag; the plain delete below says why it can't.
        }

        var disposition = new FILE_DISPOSITION_INFO { DeleteFile = 1 };
        if (!SetFileInformationByHandle(handle, FileDispositionInfo, ref disposition, Marshal.SizeOf<FILE_DISPOSITION_INFO>()))
            throw Failure($"Can't delete {full}", Marshal.GetLastPInvokeError());
    }

    /// The held entry's reparse tag, or null when it isn't a reparse point; 0, which counts as a link, when it can't be
    /// read.
    static uint? Link(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out var info, Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>()))
            return 0;
        return ((FileAttributes)info.FileAttributes).HasFlag(FileAttributes.ReparsePoint) ? info.ReparseTag : null;
    }

    static Exception Failure(string what, int error)
    {
        var message = $"{what}: {Marshal.GetPInvokeErrorMessage(error)}";
        return error == ERROR_ACCESS_DENIED ? new UnauthorizedAccessException(message) : new IOException(message);
    }

    static IOException NotAFile(string full) => new($"The editor won't delete {full}: it is a folder, not a file.");
}
