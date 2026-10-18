using System.Text.Json;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Codex;

/// One Codex usage-limit window: percent used (0–100), its length in minutes, and when it resets (Unix seconds).
public sealed record CodexWindow(double UsedPct, double? WindowMinutes, double? ResetsAt);

/// The limits from one token_count event, and when it was logged (Unix seconds).
public sealed record CodexLimits(CodexWindow? Primary, CodexWindow? Secondary, double? At);

/// Codex's token_count events in rollout logs. Undocumented; observed on 2026-10-17:
/// `{"timestamp":…,"type":"event_msg","payload":{"type":"token_count","info":…,"rate_limits":{"limit_id":"codex",
/// "primary":{"used_percent":52.0,"window_minutes":10080,"resets_at":1792665217},"secondary":null,…}}}`.
/// Which window is primary depends on the plan (a 5-hour one, or only the weekly one). Older Codex versions wrote
/// `resets_in_seconds` instead of `resets_at`. Events for other limits (`limit_id` such as "codex_<model>") are skipped:
/// only the account's main limit is shown.
public static class CodexTokenCount
{
    public const string MainLimit = "codex";

    /// The limits in a token_count line for the main limit, or null for any other line: another event type, a line
    /// Codex is still writing, an event without rate_limits, or another limit's event. Throws FormatException for a
    /// main-limit token_count event whose windows aren't in the known shape (a format change).
    public static CodexLimits? TryParse(string line)
    {
        if (!line.Contains("\"token_count\"", StringComparison.Ordinal)) return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (JsonFields.String(root, "type") != "event_msg"
                || JsonFields.Object(root, "payload") is not { } payload
                || JsonFields.String(payload, "type") != "token_count"
                || JsonFields.Object(payload, "rate_limits") is not { } limits)
                return null;

            if (JsonFields.Has(limits, "limit_id") && JsonFields.String(limits, "limit_id") != MainLimit) return null;

            var at = JsonFields.UnixSeconds(root, "timestamp");
            var primary = Window(limits, "primary", at);
            var secondary = Window(limits, "secondary", at);
            if (primary is null && secondary is null && !limits.TryGetProperty("primary", out _))
                throw new FormatException("a Codex token_count event's rate_limits has no primary window");
            return new CodexLimits(primary, secondary, at);
        }
    }

    /// Writes codex.primary.* and codex.secondary.* (pct, resets_at, window_min) and codex.updated_at. A window's keys
    /// expire when it resets, since its percentage is out of date from then on.
    public static void Apply(CodexLimits limits, SensorStore store, DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeMilliseconds() / 1000.0;
        Write(store, "codex.primary", limits.Primary, nowSeconds);
        Write(store, "codex.secondary", limits.Secondary, nowSeconds);
        if (limits.At is { } at)
            store.Set("codex.updated_at", Math.Floor(at));
    }

    static CodexWindow? Window(JsonElement limits, string name, double? at)
    {
        if (!JsonFields.Has(limits, name)) return null;
        var window = JsonFields.Object(limits, name)
            ?? throw new FormatException($"a Codex token_count event's rate_limits.{name} is not an object");
        var used = JsonFields.Number(window, "used_percent")
            ?? throw new FormatException($"a Codex token_count event's rate_limits.{name} has no used_percent number");

        var resetsAt = JsonFields.UnixSeconds(window, "resets_at");
        if (resetsAt is null && JsonFields.Number(window, "resets_in_seconds") is { } seconds && at is { } logged)
            resetsAt = JsonFields.IsPlausibleUnixSeconds(logged + seconds) ? logged + seconds : null;
        return new CodexWindow(used, JsonFields.Number(window, "window_minutes"), resetsAt);
    }

    static void Write(SensorStore store, string prefix, CodexWindow? window, double nowSeconds)
    {
        if (window is null) return;

        TimeSpan? ttl = null;
        if (window.ResetsAt is { } resetsAt)
        {
            if (resetsAt <= nowSeconds) return;
            ttl = TimeSpan.FromSeconds(resetsAt - nowSeconds);
            store.Set($"{prefix}.resets_at", resetsAt, ttl);
        }

        store.Set($"{prefix}.pct", window.UsedPct, ttl);
        if (window.WindowMinutes is { } minutes)
            store.Set($"{prefix}.window_min", minutes, ttl);
    }
}
