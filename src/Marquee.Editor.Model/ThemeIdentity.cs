using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Marquee.Editor.Model;

/// Which theme folder a path means, whatever way it is written (case, a trailing separator, `..`, a short 8.3 name), as
/// a name for the files the editor keeps per theme and for its one-editor-per-theme mutex. Themes in different folders
/// can share a name (two `mine`s), so the name has a short hash of the folder's full path after it.
///
/// A folder reached through a junction is a different path, so it gets a different identity: the editor won't save
/// through one (ReparseTags), and ThemeOpening opens such a folder read-only.
public static class ThemeIdentity
{
    const int MaxNameLength = 40;

    /// The folder's full path: rooted, `.` and `..` gone, short names long, no trailing separator. Its case is as given:
    /// Windows paths ignore it, so Key and MutexName do too.
    public static string FullPath(string themeFolder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(themeFolder));
        return LongPath(full) ?? full;
    }

    /// The folder's name, made safe and lowercase, then a hyphen and 8 hex digits of its full path's hash:
    /// `example-showcase-1a2b3c4d`. The same for every way of writing the one folder.
    public static string Key(string themeFolder)
    {
        var full = FullPath(themeFolder);
        return $"{Readable(Path.GetFileName(full))}-{Hash(full)[..8]}";
    }

    /// The mutex an editor holds while it has the theme open: per logon session (Local\), and named for the whole hash.
    public static string MutexName(string themeFolder) => $@"Local\Marquee.Editor.{Hash(FullPath(themeFolder))}";

    /// The SHA-256 of the full path, uppercase, as lowercase hex.
    static string Hash(string full) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant()))).ToLowerInvariant();

    /// Letters and digits, with a hyphen for each run of anything else; "theme" when nothing is left (a drive's root).
    static string Readable(string name)
    {
        var readable = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '.') readable.Append(c);
            else if (readable.Length > 0 && readable[^1] != '-') readable.Append('-');
        }

        var text = readable.ToString().Trim('-', '.');
        if (text.Length > MaxNameLength) text = text[..MaxNameLength].TrimEnd('-', '.');
        return text.Length == 0 ? "theme" : text;
    }

    /// The path with every short (8.3) name in its long form; null when it can't be read (it doesn't exist).
    static string? LongPath(string path) => CallWithBuffer(path, GetLongPathName);

    /// The path with short names where the volume has them, for tests; null when it can't be read.
    internal static string? ShortPath(string path) => CallWithBuffer(path, GetShortPathName);

    static string? CallWithBuffer(string path, Func<string, char[], int, int> call)
    {
        var buffer = new char[260];
        while (true)
        {
            var length = call(path, buffer, buffer.Length);
            if (length == 0) return null;
            if (length < buffer.Length) return new string(buffer, 0, length);
            buffer = new char[length];
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetLongPathName(string shortPath, char[] longPath, int bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetShortPathName(string longPath, char[] shortPath, int bufferLength);
}
