using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using static Marquee.Core.Files.FileNative;

namespace Marquee.Core.Files;

/// A copy under a name nothing has yet, and a delete, both refusing links as writes do: for the theme editor, which
/// copies files into a theme folder and deletes the ones nothing uses.
public static partial class SafeFile
{
    /// Copies `source` to `destination`, a name nothing has yet, making missing folders. The copy goes to a temporary
    /// file in the destination's folder, held as WriteAllBytes holds it, and is then renamed to its name without
    /// replacing anything, so a reader never sees part of it. False, leaving nothing behind, when something (a file, a
    /// folder or a link) already has the name: the caller tries another. Throws UnsafePathException when the
    /// destination's path holds a link, or the IOException or UnauthorizedAccessException of a failed read or write.
    /// The source is read wherever it is: it is the file the user chose.
    public static bool TryCopyNew(string source, string destination)
    {
        var (full, folder) = FileAndFolder(destination);
        ThrowIfLinked(full);

        using var held = HoldFolder(folder);
        // Checked first so a large file isn't copied for a name already in use; the rename checks again.
        if (Taken(full)) return false;

        var temporary = Path.Combine(folder, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        bool named;
        try
        {
            using (var stream = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.Delete,
                FileShare.Read, bufferSize: 81920, FileOptions.None, fileSecurity: null))
            {
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920,
                    FileOptions.SequentialScan))
                {
                    input.CopyTo(stream);
                }

                stream.Flush(flushToDisk: true);
                named = RenameNew(stream.SafeFileHandle, held, full);
            }
        }
        catch
        {
            DeleteQuietly(temporary);
            throw;
        }

        if (!named) DeleteQuietly(temporary);
        return named;
    }

    /// Deletes the file at `path`; nothing to do when it is gone. Refused with UnsafePathException when any part of the
    /// path, or the file itself, is a reparse point: deleting through a junction would delete a file somewhere else. The
    /// folder is held meanwhile and the file is opened without following a reparse point, so a link made after the
    /// check is refused too, never followed. A folder isn't deleted (IOException). A read-only file is refused
    /// (UnauthorizedAccessException) unless `evenReadOnly`: then it goes, by the handle already checked, without its
    /// attribute being changed first, where Windows can (10 1809 and later, on NTFS), and is refused elsewhere.
    public static void Delete(string path, bool evenReadOnly = false)
    {
        var (full, folder) = FileAndFolder(path);
        ThrowIfLinked(full, "delete");
        if (!Directory.Exists(folder)) return;

        using var held = OpenFolder(folder);
        // DELETE access, shared as careful readers share, and no backup semantics: a folder fails to open.
        using var handle = CreateFile(full, DELETE | FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND) return;
            if (Directory.Exists(full)) throw NotAFile(full);
            var message = $"Can't delete {full}: {Marshal.GetPInvokeErrorMessage(error)}";
            throw error == ERROR_ACCESS_DENIED ? new UnauthorizedAccessException(message) : new IOException(message);
        }

        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException($"Can't read {full}'s attributes: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
        var attributes = (FileAttributes)info.FileAttributes;
        if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw Linked(full, full, "delete");
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
        {
            var error = Marshal.GetLastPInvokeError();
            var message = $"Can't delete {full}: {Marshal.GetPInvokeErrorMessage(error)}";
            throw error == ERROR_ACCESS_DENIED ? new UnauthorizedAccessException(message) : new IOException(message);
        }
    }

    /// Whether anything, a dangling link included, has the name.
    static bool Taken(string full)
    {
        try
        {
            File.GetAttributes(full); // the entry's own attributes, so a link to nothing still counts
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// Renames the open temporary file to `full`'s name in the held folder, by the folder's handle as RenameOver does,
    /// but never over anything: false when the name is taken.
    static bool RenameNew(SafeFileHandle file, SafeFileHandle folder, string full)
    {
        var name = Path.GetFileName(full);
        var error = Rename(file, folder, name, FileRenameInformationEx, FILE_RENAME_POSIX_SEMANTICS);
        if (error is ERROR_INVALID_PARAMETER or ERROR_NOT_SUPPORTED or ERROR_INVALID_FUNCTION)
            error = Rename(file, folder, name, FileRenameInformation, 0); // a file system without POSIX renames
        if (error == 0) return true;
        if (error is ERROR_ALREADY_EXISTS or ERROR_FILE_EXISTS) return false;
        throw new IOException($"Can't name the copy {full}: {Marshal.GetPInvokeErrorMessage(error)}");
    }

    static IOException NotAFile(string full) => new($"Marquee won't delete {full}: it is a folder, not a file.");
}
