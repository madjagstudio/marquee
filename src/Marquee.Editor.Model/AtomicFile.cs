using System.Runtime.InteropServices;
using static Marquee.Editor.Model.EditorNative;

namespace Marquee.Editor.Model;

/// The editor's own atomic writes into a theme folder, in place of SafeFile's, which stays strict for the elevated tray
/// (ReparseTags says why the editor needn't be). The new bytes go to a temporary file in the same folder, flushed to
/// disk, which then takes the old file's place through File.Replace: a reader sees the old file or the new one, never
/// part of either. File.Replace keeps what the old file was to the file system (its creation time, its permissions,
/// and for OneDrive the same item, so its version history goes on), where a rename over it would make a new file.
///
/// Nothing here makes a folder or a missing file: a theme folder moved or deleted while the editor is open stays gone
/// (DirectoryNotFoundException, FileNotFoundException). Links are the caller's to refuse first (ReparseTags.FirstLink).
internal static class AtomicFile
{
    const int ERROR_SHARING_VIOLATION = 32;
    const int ERROR_LOCK_VIOLATION = 33;
    const int ERROR_UNABLE_TO_REMOVE_REPLACED = 1175;

    /// How long, in turn, a replace that another program's open file stops is waited out before it fails: about a
    /// third of a second in all, as SafeFile waits.
    public static readonly int[] RetryDelaysMs = [10, 20, 40, 80, 160];

    /// Writes `bytes` to a new temporary file in `path`'s folder, flushed to disk, and returns its path: a hidden name
    /// nobody can guess, which the theme's watchers ignore. Throws DirectoryNotFoundException when the folder is gone.
    public static string WriteTemporary(string path, byte[] bytes)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return temporary;
        }
        catch
        {
            DeleteQuietly(temporary);
            throw;
        }
    }

    /// Puts the temporary file `temporary` in the place of `path`, which must exist (FileNotFoundException when it
    /// doesn't, and nothing is made). False when another program has `path` open without sharing it for deleting: the
    /// caller waits a moment (RetryDelaysMs) and tries again. Throws the IOException or UnauthorizedAccessException of
    /// any other failure (a read-only file, a denied folder), and `path` is as it was.
    public static bool TryReplace(string temporary, string path)
    {
        try
        {
            File.Replace(temporary, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return true;
        }
        catch (IOException ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException
                                     && (ex.HResult & 0xFFFF) is ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION or ERROR_UNABLE_TO_REMOVE_REPLACED)
        {
            return false;
        }
    }

    /// Writes `bytes` as `path`, a name nothing has yet, atomically: a temporary file in its folder, flushed, then
    /// renamed to the name without replacing anything (MoveFileEx, writing through). False, leaving nothing behind, when
    /// something has the name. For a file the user chose; the folder must exist, and isn't made
    /// (DirectoryNotFoundException). Throws the IOException or UnauthorizedAccessException of any other failure.
    public static bool TryWriteNew(string path, byte[] bytes)
    {
        var temporary = WriteTemporary(path, bytes);
        try
        {
            if (MoveFileEx(temporary, path, MOVEFILE_WRITE_THROUGH)) return true;
            var error = Marshal.GetLastPInvokeError();
            try
            {
                File.GetAttributes(path); // the entry's own attributes, so a link to nothing has the name too
                return false;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Nothing has the name: the rename failed for another reason, said below.
            }

            var message = $"Can't write {path}: {Marshal.GetPInvokeErrorMessage(error)}";
            throw error == ERROR_ACCESS_DENIED ? new UnauthorizedAccessException(message) : new IOException(message);
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    /// Writes `bytes` as `path`, made or replaced, atomically: a temporary file in its folder, flushed, then renamed
    /// over it (MoveFileEx, writing through). For a file the user chose; the folder must exist, and isn't made
    /// (DirectoryNotFoundException). Throws the IOException or UnauthorizedAccessException of a failure, and `path` is
    /// as it was.
    public static void WriteOver(string path, byte[] bytes)
    {
        var temporary = WriteTemporary(path, bytes);
        try
        {
            if (MoveFileEx(temporary, path, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) return;
            var error = Marshal.GetLastPInvokeError();
            var message = $"Can't write {path}: {Marshal.GetPInvokeErrorMessage(error)}";
            throw error == ERROR_ACCESS_DENIED ? new UnauthorizedAccessException(message) : new IOException(message);
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    /// Copies `source` to `destination`, a name nothing has yet, in a folder that must exist (it isn't made:
    /// DirectoryNotFoundException): a temporary file beside it, flushed, then renamed to its name without replacing
    /// anything (MoveFileEx, writing through), so a reader never sees part of it. False, leaving nothing behind, when
    /// something (a file, a folder or a link) already has the name: the caller tries another. Throws the IOException or
    /// UnauthorizedAccessException of a failed read or write. The source is read wherever it is: the user chose it.
    public static bool TryCopyNew(string source, string destination)
    {
        // Checked first so a large file isn't copied for a name already in use; the rename checks again.
        if (Taken(destination)) return false;
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
            {
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                           FileOptions.SequentialScan))
                {
                    input.CopyTo(output);
                }

                output.Flush(flushToDisk: true);
            }

            if (MoveFileEx(temporary, destination, MOVEFILE_WRITE_THROUGH)) return true;
            var error = Marshal.GetLastPInvokeError();
            if (error is ERROR_ALREADY_EXISTS or ERROR_FILE_EXISTS) return false;
            var message = $"Can't name the copy {destination}: {Marshal.GetPInvokeErrorMessage(error)}";
            throw error == ERROR_ACCESS_DENIED ? new UnauthorizedAccessException(message) : new IOException(message);
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    /// Makes the folder `path` if it is missing, but never the folder it is in: a theme folder moved or deleted while
    /// open isn't made again around a new images folder (DirectoryNotFoundException). Throws IOException when a file
    /// has the name, or the IOException or UnauthorizedAccessException of a failure.
    public static void MakeFolder(string path)
    {
        if (CreateDirectory(path, IntPtr.Zero)) return;
        var error = Marshal.GetLastPInvokeError();
        if (error == ERROR_ALREADY_EXISTS && Directory.Exists(path)) return;
        var message = $"Can't make the folder {path}: {Marshal.GetPInvokeErrorMessage(error)}";
        throw error switch
        {
            ERROR_PATH_NOT_FOUND => new DirectoryNotFoundException(message),
            ERROR_ACCESS_DENIED => new UnauthorizedAccessException(message),
            _ => new IOException(message),
        };
    }

    /// Whether anything, a dangling link included, has the name.
    static bool Taken(string path)
    {
        try
        {
            File.GetAttributes(path); // the entry's own attributes, so a link to nothing still counts
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// Deletes a temporary file; nothing to do when it was renamed into place or never made.
    public static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A hidden temporary file left in the theme folder is harmless, and the caller has a failure to report.
        }
    }
}
