using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static Marquee.Core.Files.FileNative;

namespace Marquee.Core.Files;

/// Writes for an elevated Marquee into folders the user's unelevated programs control, such as config.yaml in
/// %LOCALAPPDATA%\Marquee. Such a program could put a junction or symbolic link on the path, and the elevated write
/// would land wherever it points: a quiet way round UAC. So a write is refused when any existing part of the path, or
/// the file itself, is a reparse point. That check alone could be raced (a link made a moment after it), so the folder
/// is then opened and held, which stops it and every folder above it from being renamed or replaced, and is refused
/// unless it really is where the path says. The new contents go to a temporary file in that folder, which is renamed
/// over the old file, so a reader sees the old file or the new one, never part of either.
///
/// Every reparse point is refused, not only links. OneDrive's placeholders are reparse points too, but %LOCALAPPDATA%
/// is never synced, an elevated write has no business in a synced folder, and other kinds belong to file system
/// filters that may redirect too.
public static partial class SafeFile
{
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// Writes `contents` as UTF-8 without a byte order mark, making missing folders. Throws UnsafePathException when the
    /// path holds a link, or the IOException or UnauthorizedAccessException of a write that failed, which leaves the old
    /// file as it was.
    public static void WriteAllText(string path, string contents) => WriteAllBytes(path, Utf8.GetBytes(contents));

    /// WriteAllText for bytes.
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var (full, folder) = FileAndFolder(path);
        ThrowIfLinked(full);

        using var held = HoldFolder(folder);
        Replace(held, folder, full, bytes);
    }

    /// Reads the file's text (as File.ReadAllText does; "" for a missing file), passes it to `edit` and replaces the
    /// file with what that returns, as WriteAllText does, all while the folder is held: an elevated read through a link
    /// would hand the user an administrator's file once the edit is written back where they can read it. So the file
    /// is refused, before anything is read, when the path holds a link, when the file itself is a reparse point, or when
    /// it has another hard link (another name for the same file, which may be one the user can't read). Nothing is
    /// written when `edit` returns the text unchanged. Missing folders are made, as for WriteAllText. An exception from
    /// `edit` leaves the file as it was.
    public static void Edit(string path, Func<string, string> edit)
    {
        var (full, folder) = FileAndFolder(path);
        ThrowIfLinked(full);

        using var held = HoldFolder(folder);
        var text = ReadUnlinked(full) ?? "";
        var edited = edit(text);
        if (edited != text) Replace(held, folder, full, Utf8.GetBytes(edited));
    }

    /// Makes a folder and any missing folder above it, refused as WriteAllText refuses a path with a link on it: for an
    /// elevated Marquee making a folder inside one the user's programs control, such as %LOCALAPPDATA%\Marquee\themes,
    /// where a junction would otherwise have an administrator make the folder wherever it points.
    public static void CreateDirectory(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        ThrowIfLinked(full);
        HoldFolder(full).Dispose();
    }

    /// The file's text, or null when there is no file. It is opened without following a reparse point (a link made
    /// after ThrowIfLinked looked is opened as the link, not its target) and refused if it is one, or if it has another
    /// hard link. Edit calls this with the folder held, so the path can't be redirected above the file meanwhile.
    internal static string? ReadUnlinked(string full)
    {
        // Shared for writing and deleting, as a careful reader opens a file others replace. No backup semantics: a
        // folder (or a junction) in the file's place fails to open rather than being read as one.
        using var handle = CreateFile(full, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero,
            OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == ERROR_FILE_NOT_FOUND) return null;
            if (IsReparsePoint(full)) throw Linked(full, full);
            var message = $"Can't read {full}: {Marshal.GetPInvokeErrorMessage(error)}";
            throw error == ERROR_ACCESS_DENIED ? new UnauthorizedAccessException(message) : new IOException(message);
        }

        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException($"Can't read {full}'s attributes: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
        if (((FileAttributes)info.FileAttributes).HasFlag(FileAttributes.ReparsePoint)) throw Linked(full, full);
        if (info.NumberOfLinks > 1)
            throw new UnsafePathException(
                $"Marquee won't change {full}: it is a hard link, another name for a file that may be somewhere else. Replace it with a copy of the file.");

        using var stream = new FileStream(handle, FileAccess.Read);
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    static (string Full, string Folder) FileAndFolder(string path)
    {
        var full = Path.GetFullPath(path);
        return (full, Path.GetDirectoryName(full) ?? throw new ArgumentException($"{path} is not a file.", nameof(path)));
    }

    /// Whether the entry itself, not what it may point to, is a reparse point; false when it can't be read.
    static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// Writes `bytes` to a new file in the held folder and renames it over `full`.
    static void Replace(SafeFileHandle held, string folder, string full, byte[] bytes)
    {
        // A name nobody can guess, so nothing can be waiting there; CreateNew fails rather than open what is.
        var temporary = Path.Combine(folder, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            // Open for deleting, which renaming by handle takes, and shared for reading, since it is the file readers
            // open from the moment it is renamed until this handle closes.
            using var stream = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.Delete,
                FileShare.Read, bufferSize: 4096, FileOptions.None, fileSecurity: null);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            RenameOver(stream.SafeFileHandle, held, full);
        }
        catch
        {
            DeleteQuietly(temporary);
            throw;
        }
    }

    /// Renames the open temporary file to `full`'s name in the held folder: by the folder's handle, not its path, so it
    /// lands there whatever the path means by now, and replacing whatever has the name, a link included, without
    /// writing through it. With POSIX semantics, a reader that has the old file open (sharing it for deleting, as
    /// careful readers do) keeps reading it and doesn't stop the rename. A reader that doesn't share it does, and is
    /// waited out for a moment.
    static void RenameOver(SafeFileHandle file, SafeFileHandle folder, string full)
    {
        int[] retryDelaysMs = [10, 20, 40, 80, 160, 320];
        var name = Path.GetFileName(full);
        for (var attempt = 0; ; attempt++)
        {
            var error = Rename(file, folder, name, FileRenameInformationEx, FILE_RENAME_REPLACE_IF_EXISTS | FILE_RENAME_POSIX_SEMANTICS);
            if (error is ERROR_INVALID_PARAMETER or ERROR_NOT_SUPPORTED or ERROR_INVALID_FUNCTION)
                error = Rename(file, folder, name, FileRenameInformation, 1); // a file system without POSIX renames
            if (error == 0) return;
            if (error is ERROR_SHARING_VIOLATION or ERROR_ACCESS_DENIED && attempt < retryDelaysMs.Length)
            {
                Thread.Sleep(retryDelaysMs[attempt]);
                continue;
            }

            throw new IOException($"Can't replace {full}: {Marshal.GetPInvokeErrorMessage(error)}");
        }
    }

    /// The Win32 error, or 0.
    static int Rename(SafeFileHandle file, SafeFileHandle folder, string name, int infoClass, uint flags)
    {
        var nameOffset = (int)Marshal.OffsetOf<FILE_RENAME_INFORMATION>(nameof(FILE_RENAME_INFORMATION.FileNameLength)) + sizeof(uint);
        // The structure's own size counts one character of the name and the padding after it; the name follows.
        var size = (nameOffset + sizeof(char) + IntPtr.Size - 1) / IntPtr.Size * IntPtr.Size + name.Length * sizeof(char);
        var buffer = Marshal.AllocHGlobal(size);
        var added = false;
        try
        {
            Marshal.Copy(new byte[size], 0, buffer, size);
            folder.DangerousAddRef(ref added);
            Marshal.StructureToPtr(new FILE_RENAME_INFORMATION
            {
                Flags = flags,
                RootDirectory = folder.DangerousGetHandle(),
                FileNameLength = (uint)(name.Length * sizeof(char)),
            }, buffer, fDeleteOld: false);
            Marshal.Copy(name.ToCharArray(), 0, buffer + nameOffset, name.Length);
            var status = NtSetInformationFile(file, out _, buffer, size, infoClass);
            return status == 0 ? 0 : RtlNtStatusToDosError(status);
        }
        finally
        {
            if (added) folder.DangerousRelease();
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// Opens a folder and holds it: while the handle is open, neither it nor any folder above it can be renamed, so
    /// paths through it keep meaning the same folder. Refused when the folder is a reparse point or isn't where its path
    /// says (a link further up). Short names (C:\Users\RUNNER~1) are the same folder, not a link.
    internal static SafeFileHandle OpenFolder(string folder)
    {
        // Listing access, not just attributes: Windows checks the share mode, which leaves out FILE_SHARE_DELETE, only
        // against opens that read or write.
        var handle = CreateFile(folder, FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException($"Can't open {folder}: {Marshal.GetPInvokeErrorMessage(error)}");
        }

        try
        {
            if (!GetFileInformationByHandleEx(handle, FileBasicInfo, out var info, Marshal.SizeOf<FILE_BASIC_INFO>()))
                throw new IOException($"Can't read {folder}'s attributes: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
            var attributes = (FileAttributes)info.FileAttributes;
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw Linked(folder, folder);
            if (!attributes.HasFlag(FileAttributes.Directory)) throw new IOException($"{folder} is a file, not a folder.");

            var really = FinalPath(handle, folder);
            if (!string.Equals(really, LongPath(folder), StringComparison.OrdinalIgnoreCase))
                throw new UnsafePathException(
                    $"Marquee won't write in {folder}: a junction or link on its path leads to {really} instead.");
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// Throws for the first part of the path, from the root down, that is a reparse point. Parts that don't exist yet
    /// are fine: HoldFolder makes them. `action` is what is refused, for the message: "write" or "delete".
    static void ThrowIfLinked(string full, string action = "write")
    {
        var root = Path.GetPathRoot(full)!;
        var path = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, part);
            FileAttributes attributes;
            try
            {
                // The attributes of the entry itself: a link's own, not its target's.
                attributes = File.GetAttributes(path);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return;
            }

            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw Linked(full, path, action);
        }
    }

    /// Holds `folder`, making it and any missing folder above it first. Each missing folder is made only while the one
    /// above it is held and checked, so it can't land anywhere else.
    static SafeFileHandle HoldFolder(string folder)
    {
        var missing = new Stack<string>();
        var existing = folder;
        while (!Directory.Exists(existing))
        {
            missing.Push(existing);
            existing = Path.GetDirectoryName(existing) ?? throw new DirectoryNotFoundException($"{folder}'s drive doesn't exist.");
        }

        var held = OpenFolder(existing);
        try
        {
            while (missing.TryPop(out var next))
            {
                Directory.CreateDirectory(next);
                var inside = OpenFolder(next); // before letting go of the folder above, so nothing can move in between
                held.Dispose();
                held = inside;
            }

            return held;
        }
        catch
        {
            held.Dispose();
            throw;
        }
    }

    static UnsafePathException Linked(string target, string link, string action = "write") =>
        new($"Marquee won't {action} {target}: {link} is a junction, a symbolic link or another reparse point, which could send the {action} somewhere else.");

    /// Where the handle really is, as C:\… or \\server\share\….
    static string FinalPath(SafeFileHandle handle, string folder)
    {
        var buffer = new char[1024];
        while (true)
        {
            var length = GetFinalPathNameByHandle(handle, buffer, buffer.Length, 0);
            if (length == 0)
                throw new IOException($"Can't tell where {folder} is: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
            if (length < buffer.Length) return Plain(new string(buffer, 0, length));
            buffer = new char[length];
        }
    }

    /// The path with every short (8.3) name in its long form, which is how FinalPath names it.
    static string LongPath(string path)
    {
        var buffer = new char[1024];
        while (true)
        {
            var length = GetLongPathName(path, buffer, buffer.Length);
            if (length == 0)
                throw new IOException($"Can't read {path}'s long name: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
            if (length < buffer.Length) return Plain(new string(buffer, 0, length));
            buffer = new char[length];
        }
    }

    static string Plain(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        return Path.TrimEndingDirectorySeparator(path);
    }

    static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do; the caller is already reporting a failure.
        }
    }
}
