using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Claude;

/// One `~/.claude/sessions/<pid>.json` file. Undocumented; observed on 2026-10-17 (Claude Code 2.1.299):
/// `pid`, `procStart` (the process's start time as a Windows FILETIME, in a string), `pidDomain` ("win32:<host>"),
/// `status` ("busy" or "idle"), and fields Marquee doesn't use.
public sealed record ClaudeSessionFile(int Pid, string? Status, long? ProcStart, string? PidDomain)
{
    /// Null when the text isn't a session file: not JSON, not an object, or no positive integer `pid`.
    public static ClaudeSessionFile? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (JsonFields.Number(root, "pid") is not { } pid || pid != Math.Floor(pid) || pid is <= 0 or > int.MaxValue)
                return null;

            long? procStart = JsonFields.String(root, "procStart") is { } text && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                ? ticks
                : JsonFields.Number(root, "procStart") is { } number ? (long)number : null;
            return new ClaudeSessionFile((int)pid, JsonFields.String(root, "status"), procStart, JsonFields.String(root, "pidDomain"));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// A running process, and when it started (null if Windows wouldn't say, e.g. an elevated process seen from a normal one).
public readonly record struct RunningProcess(DateTime? StartUtc);

/// Counts Claude Code sessions from ~/.claude/sessions: claude.sessions.total, .busy and .idle. A file whose process
/// has exited (or whose pid now belongs to a different process) is a leftover and isn't counted. A status other than
/// busy or idle counts toward the total only. `hostNames` are the names this PC answers to: a file whose `pidDomain`
/// names another host is from another PC and isn't counted.
public sealed class ClaudeSessionsProvider(string folder, Func<int, RunningProcess?> findProcess, IReadOnlyCollection<string> hostNames) : ISensorProvider
{
    /// Windows truncates the NetBIOS name (Environment.MachineName) to 15 characters; Claude Code may write the full host name.
    const int NetBiosLength = 15;

    /// Session files are under a kilobyte; anything much larger isn't one.
    const int MaxFileBytes = 64 * 1024;
    const int MaxFiles = 1000;

    /// A FILETIME is in 100 ns ticks; allow a second either way between the file and what Windows reports.
    const long StartTolerance = TimeSpan.TicksPerSecond;

    static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);

    public ClaudeSessionsProvider() : this(AgentPaths.ClaudeSessions, FindProcess, ThisPc()) { }

    /// The names this PC answers to: the NetBIOS name and the full host name (they differ on a PC whose name is over 15 characters).
    internal static IReadOnlyCollection<string> ThisPc() =>
        new[] { Environment.MachineName, Dns.GetHostName() }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public string Name => "Claude Code sessions";
    public TimeSpan Interval => TimeSpan.FromSeconds(2);

    public void Poll(SensorStore store)
    {
        int total = 0, busy = 0, idle = 0, files = 0, unreadable = 0;
        if (Directory.Exists(folder))
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.json").Take(MaxFiles))
            {
                // Gone, or being rewritten (read as empty); the next poll sees it.
                if (Read(path) is not { } text || string.IsNullOrWhiteSpace(text)) continue;
                files++;
                if (ClaudeSessionFile.Parse(text) is not { } session)
                {
                    unreadable++;
                    continue;
                }

                if (!IsAlive(session)) continue;
                total++;
                if (session.Status == "busy") busy++;
                else if (session.Status == "idle") idle++;
            }
        }

        if (files > 0 && unreadable == files)
            throw new FormatException($"none of the {files} files in {folder} look like Claude Code sessions any more (no numeric pid); " +
                "the format may have changed.");

        store.Set("claude.sessions.total", total, Ttl);
        store.Set("claude.sessions.busy", busy, Ttl);
        store.Set("claude.sessions.idle", idle, Ttl);
    }

    public void Dispose() { }

    bool IsAlive(ClaudeSessionFile session)
    {
        // A session file from another PC (a synced home folder, say) names a pid that means nothing here.
        if (session.PidDomain is { } domain && domain.StartsWith("win32:", StringComparison.Ordinal)
            && !IsThisPc(domain[6..]))
            return false;

        if (findProcess(session.Pid) is not { } process) return false;
        if (session.ProcStart is not { } fileTime || process.StartUtc is not { } started) return true;
        return Math.Abs(started.ToFileTimeUtc() - fileTime) <= StartTolerance;
    }

    /// True when `host` is one of this PC's names, or a longer name whose first 15 characters are one (its NetBIOS form).
    bool IsThisPc(string host) =>
        hostNames.Any(name => name.Equals(host, StringComparison.OrdinalIgnoreCase)
            || (host.Length > NetBiosLength && name.Equals(host[..NetBiosLength], StringComparison.OrdinalIgnoreCase)));

    static string? Read(string path)
    {
        try
        {
            // Share everything, so Claude Code can rewrite or delete the file while it is open here.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxFileBytes) return "{}"; // too big to be a session: unreadable, not empty
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static RunningProcess? FindProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            try
            {
                // GetProcessById can return a process that has exited but whose handle is still held elsewhere.
                if (process.HasExited) return null;
                return new RunningProcess(process.StartTime.ToUniversalTime());
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                return new RunningProcess(null); // running, but Windows won't say more (access denied)
            }
        }
        catch (ArgumentException)
        {
            return null; // no process with that id
        }
    }
}
