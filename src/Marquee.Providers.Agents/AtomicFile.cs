using System.Text;

namespace Marquee.Providers.Agents;

/// Writes that leave either the old file or the new one, never half of each: write a temporary file beside the
/// target, then swap it in. A failed write leaves no temporary file behind.
static class AtomicFile
{
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// Writes `path`. With overwrite false, a file that already exists there (even one created a moment ago) is never
    /// replaced: the move fails with an IOException instead.
    public static void WriteAllText(string path, string text, bool overwrite = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".marquee-tmp";
        try
        {
            File.WriteAllText(temporary, text, Utf8);
            File.Move(temporary, path, overwrite);
        }
        catch
        {
            DeleteQuietly(temporary);
            throw;
        }
    }

    /// Replaces an existing file, keeping its previous contents at `backup`. If the swap fails halfway and leaves
    /// `path` missing, the backup is copied back, so the user is never left without a file.
    public static void Replace(string path, string text, string backup)
    {
        var temporary = path + ".marquee-tmp";
        try
        {
            File.WriteAllText(temporary, text, Utf8);
            File.Replace(temporary, path, backup);
        }
        catch
        {
            DeleteQuietly(temporary);
            try
            {
                if (!File.Exists(path) && File.Exists(backup)) File.Copy(backup, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The original exception is the one worth reporting; the backup is still on disk.
            }

            throw;
        }
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
