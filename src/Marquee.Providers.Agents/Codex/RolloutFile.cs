using System.Text;

namespace Marquee.Providers.Agents.Codex;

/// Reads Codex rollout logs (`~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl`, one JSON event per line) without loading
/// them: a long-running thread's log passes 4 GB. Files are opened with full sharing, since Codex keeps appending.
public static class RolloutFile
{
    const int ChunkBytes = 64 * 1024;

    /// The rollout logs under `folder`, most recently written first. Empty if the folder doesn't exist.
    public static IEnumerable<FileInfo> NewestFirst(string folder) =>
        Directory.Exists(folder)
            ? new DirectoryInfo(folder).EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc)
            : [];

    /// Complete lines from the end of the file backwards, newest first, reading at most `maxBytes` from the end.
    /// The line cut by that limit is skipped, as is an empty line. A last line Codex is still writing comes back
    /// partial; callers skip lines that don't parse.
    public static IEnumerable<string> LinesFromEnd(string path, long maxBytes)
    {
        using var stream = Open(path);
        var stop = Math.Max(0, stream.Length - maxBytes);
        var position = stream.Length;
        var tail = Array.Empty<byte>(); // bytes after the newest newline seen so far: a line whose start isn't read yet
        var chunk = new byte[ChunkBytes];

        while (position > stop)
        {
            var size = (int)Math.Min(ChunkBytes, position - stop);
            position -= size;
            stream.Position = position;
            stream.ReadExactly(chunk, 0, size);

            var buffer = new byte[size + tail.Length];
            Array.Copy(chunk, buffer, size);
            Array.Copy(tail, 0, buffer, size, tail.Length);

            var end = buffer.Length;
            for (var i = buffer.Length - 1; i >= 0; i--)
            {
                if (buffer[i] != (byte)'\n') continue;
                if (Line(buffer, i + 1, end) is { } line) yield return line;
                end = i;
            }

            tail = buffer[..end];
        }

        // What's left is a whole line only if the read reached the start of the file.
        if (stop == 0 && Line(tail, 0, tail.Length) is { } first) yield return first;
    }

    /// The first line, if it is shorter than `maxBytes`. Codex starts each log with a session_meta event of ~20 KB.
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

    /// UTF-8 is safe to split at '\n' bytes: no multi-byte character contains one.
    static string? Line(byte[] buffer, int start, int end)
    {
        if (end > start && buffer[end - 1] == (byte)'\r') end--;
        return end > start ? Encoding.UTF8.GetString(buffer, start, end - start) : null;
    }
}
