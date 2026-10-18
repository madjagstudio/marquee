using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Marquee.Providers.Agents.Claude;

/// What a finished child process printed on stdout (at most ClaudeCli.MaxOutputBytes of it) and how it ended.
internal sealed record ProcessResult(int ExitCode, string Output);

/// Finds `claude.exe` and runs `claude -p /usage` with it. Print mode runs `/usage` without a model request, so it
/// costs nothing; it works for every Claude Code surface, including the desktop app, which doesn't run status lines.
public static class ClaudeCli
{
    /// More stdout than this is read and thrown away: the usage report is a few kilobytes.
    public const int MaxOutputBytes = 1024 * 1024;

    /// How long one `claude -p /usage` may take before it is stopped. It takes about 3 seconds.
    public static readonly TimeSpan UsageTimeout = TimeSpan.FromSeconds(30);

    /// The command line verified on 2026-10-18 (Claude Code 2.1.299): no session file or transcript is left behind, no
    /// hooks or MCP servers start, and only the user-level settings are read. Not `--bare`: that never reads the OAuth
    /// login, so it returns no limits. One element per argument, so no shell parses them.
    public static readonly IReadOnlyList<string> UsageArguments =
    [
        "-p", "/usage",
        "--no-session-persistence",
        "--setting-sources", "user",
        "--settings", """{"disableAllHooks":true}""",
        "--strict-mcp-config",
        "--output-format", "stream-json",
        "--verbose",
    ];

    /// How long to wait, once the child has exited, for its output to finish arriving. A background child of the
    /// command can hold the pipes open long after the command itself is done.
    static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    /// The path of `claude.exe` on this PC, or null. Searched in order: each PATH folder (full paths only: a relative one
    /// would depend on Marquee's current folder); the native installer's
    /// `~\.local\bin`; the desktop app's bundled copy, `%APPDATA%\Claude\claude-code\<version>\<hash>\claude.exe`
    /// (an undocumented layout), taking the newest version whose folder name parses.
    public static string? Find() => Find(Environment.GetEnvironmentVariable, File.Exists, Subfolders);

    /// `environment` reads an environment variable, `fileExists` tests a path, and `directories` lists the full paths
    /// of a folder's subfolders (nothing for a folder that doesn't exist).
    public static string? Find(Func<string, string?> environment, Func<string, bool> fileExists, Func<string, IEnumerable<string>> directories)
    {
        if (environment("PATH") is { } path)
        {
            foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var folder = entry.Trim('"');
                if (!Path.IsPathFullyQualified(folder)) continue;
                var candidate = Path.Combine(folder, "claude.exe");
                if (fileExists(candidate)) return candidate;
            }
        }

        if (environment("USERPROFILE") is { Length: > 0 } home)
        {
            var native = Path.Combine(home, ".local", "bin", "claude.exe");
            if (fileExists(native)) return native;
        }

        if (environment("APPDATA") is { Length: > 0 } appData)
        {
            var versions = directories(Path.Combine(appData, "Claude", "claude-code"))
                .Select(folder => (Folder: folder, Version: ParseVersion(folder)))
                .Where(v => v.Version is not null)
                .OrderByDescending(v => v.Version);
            foreach (var (folder, _) in versions)
            {
                foreach (var hash in directories(folder).OrderBy(h => h, StringComparer.OrdinalIgnoreCase))
                {
                    var candidate = Path.Combine(hash, "claude.exe");
                    if (fileExists(candidate)) return candidate;
                }
            }
        }

        return null;
    }

    /// Runs `exe -p /usage …` in `workingDirectory` (created if missing) and returns its stdout. Throws TimeoutException
    /// after `timeout` (the process and its children are killed) and InvalidOperationException naming the exit code when it
    /// fails. Never leaves the process running.
    public static string RunUsage(string exe, string workingDirectory, TimeSpan timeout) =>
        RunUsage(exe, UsageArguments, workingDirectory, timeout);

    internal static string RunUsage(string exe, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout)
    {
        Directory.CreateDirectory(workingDirectory);
        var result = RunProcess(exe, arguments, workingDirectory, timeout);
        if (result.ExitCode == 0) return result.Output;

        // "Not logged in" may come with a failing exit code; say what claude printed so the log shows why.
        var message = $"{Path.GetFileName(exe)} exited with code {result.ExitCode}";
        throw new InvalidOperationException(ClaudeUsageReport.ResultText(result.Output) is { } text
            ? $"{message}: {ClaudeUsageReport.Excerpt(text)}"
            : message);
    }

    /// Starts `exe` with the arguments (no shell), closes its stdin, reads stdout (up to MaxOutputBytes) and stderr (thrown
    /// away, but read so its pipe can't fill), and waits. At the timeout the process tree is killed.
    internal static ProcessResult RunProcess(string exe, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{Path.GetFileName(exe)} did not start");
        try
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // It exited before taking its input; the wait below sees that.
            }

            var output = new CappedBuffer(MaxOutputBytes);
            var reading = Task.Run(() => output.ReadAll(process.StandardOutput.BaseStream));
            var errors = Task.Run(() => new CappedBuffer(0).ReadAll(process.StandardError.BaseStream));

            using var limit = new CancellationTokenSource(timeout);
            try
            {
                process.WaitForExitAsync(limit.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                Task.WaitAll([reading, errors], DrainTimeout);
                throw new TimeoutException($"{Path.GetFileName(exe)} did not finish within {timeout.TotalSeconds:0} seconds and was stopped");
            }

            Task.WaitAll([reading, errors], DrainTimeout);
            return new ProcessResult(process.ExitCode, output.Text());
        }
        finally
        {
            Kill(process); // a no-op once it has exited; the safety net for any other way out
        }
    }

    static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            // It exited just now, or part of its tree can't be killed; either way it is being abandoned.
        }
    }

    static Version? ParseVersion(string folder) =>
        Version.TryParse(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), out var version) ? version : null;

    static IEnumerable<string> Subfolders(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// Output collected on a reader task, safe to copy at any moment. Bytes past the cap are read and discarded.
    sealed class CappedBuffer(int cap)
    {
        readonly object gate = new();
        readonly MemoryStream bytes = new();

        public void ReadAll(Stream stream)
        {
            var chunk = new byte[8192];
            try
            {
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    lock (gate)
                    {
                        var room = cap - (int)bytes.Length;
                        if (room > 0) bytes.Write(chunk, 0, Math.Min(read, room));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The pipe was closed under us (the process was killed or abandoned); keep what arrived.
            }
        }

        public string Text()
        {
            lock (gate) return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
        }
    }
}
