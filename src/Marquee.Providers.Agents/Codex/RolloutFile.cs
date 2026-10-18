using System.Text;

namespace Marquee.Providers.Agents.Codex;

/// Reads Codex rollout logs (`~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl`, one JSON event per line) without loading
/// them: a long-running thread's log passes 4 GB. Files are opened with full sharing, since Codex keeps appending.
public static class RolloutFile
{
    const int ChunkBytes = 64 * 1024;

    /// The rollout logs under `folder`, most recently written first. Empty if the folder doesn't exist. Can throw
    /// (IOException, UnauthorizedAccessException) if the folder disappears or a subfolder is inaccessible while it is
    /// enumerated; callers (providers) let ProviderRunner handle it.
    public static IEnumerable<FileInfo> NewestFirst(string folder) =>
        Directory.Exists(folder)
            ? new DirectoryInfo(folder).EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc)
            : [];

    /// Complete lines from the end of the file backwards, newest first, reading at most `maxBytes` from the end.
    /// The line cut by that limit is skipped, as is an empty line. A last line Codex is still writing comes back
    /// partial; callers skip lines that don't parse. Reading is lazy and linear in the bytes read: each chunk is
    /// scanned once, and the pieces of a line that spans chunks are joined once, when its start is found.
    /// Throws IOException (e.g. FileNotFoundException) if the log disappears or shrinks while being read; callers
    /// (providers) let ProviderRunner handle it.
    public static IEnumerable<string> LinesFromEnd(string path, long maxBytes)
    {
        using var stream = Open(path);
        var stop = Math.Max(0, stream.Length - maxBytes);
        var position = stream.Length;
        var pending = new List<byte[]>(); // pieces of the line whose start isn't read yet; each later piece is earlier in the file
        var chunk = new byte[ChunkBytes];

        while (position > stop)
        {
            var size = (int)Math.Min(ChunkBytes, position - stop);
            position -= size;
            stream.Position = position;
            stream.ReadExactly(chunk, 0, size);

            var end = size;
            for (var i = size - 1; i >= 0; i--)
            {
                if (chunk[i] != (byte)'\n') continue;
                if (Line(chunk, i + 1, end, pending) is { } line) yield return line;
                pending.Clear();
                end = i;
            }

            if (end > 0) pending.Add(chunk[..end]);
        }

        // What's left is a whole line only if the read reached the start of the file.
        if (stop == 0 && pending.Count > 0 && Line([], 0, 0, pending) is { } first) yield return first;
    }

    /// The file's current length, asked of an open handle. The length in a directory listing (FileInfo.Length) lags for
    /// a log Codex keeps open, like its write time. Throws IOException (e.g. FileNotFoundException) if the log is gone.
    public static long Length(string path)
    {
        using var stream = Open(path);
        return stream.Length;
    }

    /// The first line, if it is shorter than `maxBytes`. Codex starts each log with a session_meta event of ~20 KB.
    /// Throws IOException (e.g. FileNotFoundException) if the log disappears or shrinks while being read; callers
    /// (providers) let ProviderRunner handle it.
    public static string? FirstLine(string path, int maxBytes)
    {
        using var stream = Open(path);
        var buffer = new byte[(int)Math.Min(maxBytes, stream.Length)];
        stream.ReadExactly(buffer);
        var newline = Array.IndexOf(buffer, (byte)'\n');
        return newline < 0 ? null : Line(buffer, 0, newline);
    }

    static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);

    /// The line is `buffer[start..end]` followed by the pieces in `rest`, nearest piece (the last) first. Joined and
    /// decoded once, whole.
    static string? Line(byte[] buffer, int start, int end, List<byte[]> rest)
    {
        if (rest.Count == 0) return Line(buffer, start, end);
        var joined = new byte[end - start + rest.Sum(p => p.Length)];
        Array.Copy(buffer, start, joined, 0, end - start);
        var at = end - start;
        for (var i = rest.Count - 1; i >= 0; i--)
        {
            rest[i].CopyTo(joined, at);
            at += rest[i].Length;
        }
        return Line(joined, 0, joined.Length);
    }

    /// UTF-8 is safe to split at '\n' bytes: no multi-byte character contains one.
    static string? Line(byte[] buffer, int start, int end)
    {
        if (end > start && buffer[end - 1] == (byte)'\r') end--;
        return end > start ? Encoding.UTF8.GetString(buffer, start, end - start) : null;
    }
}
