using System.Text.Json;
using System.Text.Json.Nodes;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Claude;

/// The last Claude Code limits, kept in %LOCALAPPDATA%\Marquee\claude-limits.json so a restarted `marquee run` shows
/// them at once instead of waiting for `/usage` or the status line. The file holds only each window's `pct` and
/// `resets_at` and `updated_at`, as Unix seconds:
/// `{ "five_hour": { "pct": 42.5, "resets_at": … }, "seven_day": { … }, "updated_at": … }`.
public sealed class ClaudeLimitsFile(string path, ILog log)
{
    static readonly (string Name, string Prefix)[] Windows = [("five_hour", "claude.five_hour"), ("seven_day", "claude.seven_day")];
    static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    string? _saved;
    bool _saveFailed;

    public ClaudeLimitsFile(ILog log) : this(AgentPaths.ClaudeLimits, log)
    {
    }

    /// Writes the saved limits into the store, leaving out any window whose reset time has passed. Call it before any
    /// source starts, so live readings always land later and win. A missing or unreadable file writes nothing.
    public void Restore(SensorStore store, DateTimeOffset now)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            log.Info($"Claude Code limits: nothing saved at {path} yet.");
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Claude Code limits: can't read {path} ({ex.Message}); starting without the last values.");
            return;
        }

        if (!TryParse(text, out var status, out var updatedAt, out var problem))
        {
            log.Warn($"Claude Code limits: ignoring {path} ({problem}).");
            return;
        }

        var nowSeconds = now.ToUnixTimeMilliseconds() / 1000.0;
        status = new ClaudeStatus(Current(status.FiveHour, nowSeconds), Current(status.SevenDay, nowSeconds), null);
        if (status.FiveHour is null && status.SevenDay is null)
        {
            log.Info($"Claude Code limits: nothing saved in {path} is still current.");
            return;
        }

        ClaudeStatusLine.Apply(status, store, now, updatedAt!.Value);
        log.Info($"Claude Code limits: restored the last values from {path}.");
    }

    /// Saves the snapshot's limits if they differ from what the file already holds. A failure is logged once, until a
    /// save works again; it never throws.
    public void Save(SensorSnapshot snapshot)
    {
        var text = Serialize(snapshot);
        if (text == _saved) return;
        try
        {
            AtomicFile.WriteAllText(path, text);
            _saved = text;
            _saveFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_saveFailed)
                log.Warn($"Claude Code limits: can't save them to {path} ({ex.Message}); they won't survive a restart.");
            _saveFailed = true;
        }
    }

    /// The file's text for a snapshot. A window is saved only with its reset time: without one, a restart couldn't tell
    /// whether it reset in the meantime.
    internal static string Serialize(SensorSnapshot snapshot)
    {
        var root = new JsonObject();
        foreach (var (name, prefix) in Windows)
        {
            if (snapshot.TryGet($"{prefix}.pct", out var pct) && pct.Number is { } used
                && snapshot.TryGet($"{prefix}.resets_at", out var resets) && resets.Number is { } resetsAt)
                root[name] = new JsonObject { ["pct"] = used, ["resets_at"] = resetsAt };
        }

        if (root.Count > 0 && snapshot.TryGet("claude.updated_at", out var updated) && updated.Number is { } updatedAt)
            root["updated_at"] = updatedAt;
        return root.ToJsonString(Format) + "\n";
    }

    /// Reads the file's text. Anything other than the shape Serialize writes is a problem, and nothing from it is used.
    internal static bool TryParse(string text, out ClaudeStatus status, out double? updatedAt, out string problem)
    {
        status = new ClaudeStatus(null, null, null);
        updatedAt = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            problem = "it is not valid JSON";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problem = "it is not a JSON object";
                return false;
            }

            var windows = new ClaudeWindow?[Windows.Length];
            for (var i = 0; i < Windows.Length; i++)
            {
                var name = Windows[i].Name;
                if (!JsonFields.Has(root, name)) continue;
                if (JsonFields.Object(root, name) is not { } window
                    || JsonFields.Number(window, "pct") is not { } pct
                    || JsonFields.Number(window, "resets_at") is not { } resetsAt || !JsonFields.IsPlausibleUnixSeconds(resetsAt))
                {
                    problem = $"{name} is not a pct and resets_at pair";
                    return false;
                }

                windows[i] = new ClaudeWindow(pct, resetsAt);
            }

            status = new ClaudeStatus(windows[0], windows[1], null);
            if (status.FiveHour is null && status.SevenDay is null)
            {
                problem = "";
                return true; // every window had reset when it was saved
            }

            if (JsonFields.Number(root, "updated_at") is not { } at || !JsonFields.IsPlausibleUnixSeconds(at))
            {
                problem = "it has no updated_at time";
                return false;
            }

            updatedAt = at;
            problem = "";
            return true;
        }
    }

    static ClaudeWindow? Current(ClaudeWindow? window, double nowSeconds) =>
        window?.ResetsAt > nowSeconds ? window : null;
}
