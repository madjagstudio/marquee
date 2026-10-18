using System.Text.Json;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Claude;

/// One usage-limit window from the status line: percent used (0–100) and when it resets (Unix seconds), if known.
public sealed record ClaudeWindow(double UsedPct, double? ResetsAt);

/// What Marquee reads from one status-line update. Windows are null when Claude Code didn't send them: API-key users,
/// before the session's first response, and after a window resets.
public sealed record ClaudeStatus(ClaudeWindow? FiveHour, ClaudeWindow? SevenDay, double? ContextPct);

/// Claude Code's documented status-line JSON (https://code.claude.com/docs/en/statusline):
/// `rate_limits.five_hour` / `seven_day` with `used_percentage` and `resets_at`, and `context_window.used_percentage`.
public static class ClaudeStatusLine
{
    /// Largest status-line JSON Marquee accepts. Claude Code sends a few kilobytes.
    public const int MaxBytes = 64 * 1024;

    /// Throws FormatException when the JSON isn't a status-line object, or a limit window is there but not in the
    /// documented shape (a format change).
    public static ClaudeStatus Parse(ReadOnlySpan<byte> json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json.ToArray());
        }
        catch (JsonException ex)
        {
            throw new FormatException($"the status-line input is not valid JSON ({ex.Message})");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("the status-line input is not a JSON object");

            var limits = JsonFields.Object(root, "rate_limits");
            var context = JsonFields.Object(root, "context_window");
            return new ClaudeStatus(
                limits is { } l ? Window(l, "five_hour") : null,
                limits is { } s ? Window(s, "seven_day") : null,
                context is { } c ? JsonFields.Number(c, "used_percentage") : null);
        }
    }

    /// Writes the readings. A window's keys expire when it resets, since Claude Code stops sending a window then and
    /// its last percentage would be wrong; claude.updated_at says how old the readings are.
    public static void Apply(ClaudeStatus status, SensorStore store, DateTimeOffset now) =>
        Apply(status, store, now, Math.Floor(now.ToUnixTimeMilliseconds() / 1000.0));

    /// Apply, with claude.updated_at set to when the readings were taken (Unix seconds) instead of now: for limits
    /// restored by ClaudeLimitsFile, so it shows their real age.
    internal static void Apply(ClaudeStatus status, SensorStore store, DateTimeOffset now, double updatedAt)
    {
        var nowSeconds = now.ToUnixTimeMilliseconds() / 1000.0;
        Write(store, "claude.five_hour", status.FiveHour, nowSeconds);
        Write(store, "claude.seven_day", status.SevenDay, nowSeconds);
        if (status.ContextPct is { } context)
            store.Set("claude.context.pct", context);
        store.Set("claude.updated_at", updatedAt);
    }

    static ClaudeWindow? Window(JsonElement limits, string name)
    {
        if (!JsonFields.Has(limits, name)) return null;
        var window = JsonFields.Object(limits, name)
            ?? throw new FormatException($"rate_limits.{name} is not an object");
        var used = JsonFields.Number(window, "used_percentage")
            ?? throw new FormatException($"rate_limits.{name} has no used_percentage number");
        return new ClaudeWindow(used, JsonFields.UnixSeconds(window, "resets_at"));
    }

    static void Write(SensorStore store, string prefix, ClaudeWindow? window, double nowSeconds)
    {
        if (window is null) return;

        TimeSpan? ttl = null;
        if (window.ResetsAt is { } resetsAt)
        {
            if (resetsAt <= nowSeconds) return; // already reset: the percentage is out of date
            ttl = TimeSpan.FromSeconds(resetsAt - nowSeconds);
            store.Set($"{prefix}.resets_at", resetsAt, ttl);
        }

        store.Set($"{prefix}.pct", window.UsedPct, ttl);
    }
}
