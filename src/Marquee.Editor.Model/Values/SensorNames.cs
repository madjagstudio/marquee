using System.Globalization;
using System.Text.RegularExpressions;
using Marquee.Core.Expressions;
using Marquee.Core.Sensors;

namespace Marquee.Editor.Model.Values;

public enum SensorCategory { Cpu, Gpu, Memory, Gaming, ClaudeCode, Codex, Fans, TimeAndDate, Yours, Other }

public sealed record SensorName(string Key, string Name, SensorCategory Category, string? Unit);

/// The editor's names for readings. Keys remain the identity used by expressions and saved themes.
public static class SensorNames
{
    static readonly IReadOnlyDictionary<string, SensorName> Known = Build();

    public static SensorName For(string key)
    {
        if (Known.TryGetValue(key, out var name)) return name;
        if (key.StartsWith("fan.", StringComparison.Ordinal))
            return new(key, $"Fan: {Words(key[4..])}", SensorCategory.Fans, "RPM");
        if (key.StartsWith("custom.", StringComparison.Ordinal))
            return new(key, $"Your value: {Words(key[7..])}", SensorCategory.Yours, null);
        return new(key, Words(key), SensorCategory.Other, null);
    }

    public static string CategoryName(SensorCategory category) => category switch
    {
        SensorCategory.Cpu => "CPU",
        SensorCategory.Gpu => "GPU",
        SensorCategory.ClaudeCode => "Claude Code",
        SensorCategory.TimeAndDate => "Time and date",
        _ => category.ToString(),
    };

    /// Display-only expansion for source calculations and text templates; never written back to YAML.
    public static string InText(string text) => Regex.Replace(text,
        @"\b[a-z][a-z0-9_]*(?:\.[a-z0-9_*]+)+\b|\b(?:fps|frametime|now)\b",
        match => For(match.Value).Name, RegexOptions.CultureInvariant);

    /// Only placeholders contain readings; literal words and escaped braces belong to the theme's author.
    public static string InTemplate(string text) => Regex.Replace(text, @"\{\{|\}\}|\{[^{}]+\}",
        match => match.Value is "{{" or "}}" ? match.Value : InText(match.Value), RegexOptions.CultureInvariant);

    public static string Format(string key, SensorValue value)
    {
        if ((key == "now" || key.EndsWith(".resets_at", StringComparison.Ordinal) || key.EndsWith(".updated_at", StringComparison.Ordinal))
            && value.Number is { } seconds && seconds >= -62135596800 && seconds <= 253402300799)
            return DateTimeOffset.FromUnixTimeSeconds((long)seconds).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        if (IsBytes(key) && value.Number is { } bytes) return Formatting.Bytes(bytes);
        var text = value.Number is { } number ? number.ToString("0.##", CultureInfo.CurrentCulture) : value.Text ?? "";
        return value.Number is not null && For(key).Unit is { } unit ? $"{text} {unit}" : text;
    }

    /// The reading is a size in bytes, shown as the theme's `|bytes` helper shows it: "3.3 GB".
    public static bool IsBytes(string key) => For(key).Unit == "bytes";

    public static IReadOnlyList<IGrouping<SensorCategory, SensorName>> Grouped(IEnumerable<string> keys) =>
        keys.Distinct(StringComparer.Ordinal).Select(For).OrderBy(name => name.Category)
            .ThenBy(name => name.Name, StringComparer.OrdinalIgnoreCase).GroupBy(name => name.Category).ToList();

    static string Words(string key) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace('.', ' ').Replace('_', ' '));

    static IReadOnlyDictionary<string, SensorName> Build()
    {
        var names = new Dictionary<string, SensorName>(StringComparer.Ordinal);
        void Add(string key, string name, SensorCategory category, string? unit = null) => names.Add(key, new(key, name, category, unit));
        foreach (var (prefix, category) in new[] { ("cpu", SensorCategory.Cpu), ("gpu", SensorCategory.Gpu) })
        {
            var title = prefix.ToUpperInvariant();
            Add(prefix + ".temp", title + " temperature", category, "°C");
            Add(prefix + ".load", title + " usage", category, "%");
            Add(prefix + ".clock", title + " clock speed", category, "MHz");
            Add(prefix + ".power", title + " power draw", category, "W");
            Add(prefix + ".voltage", title + " voltage", category, "V");
        }
        Add("gpu.fan", "GPU fan speed", SensorCategory.Gpu, "RPM");
        Add("gpu.vram_used", "GPU memory used", SensorCategory.Gpu, "bytes");
        Add("ram.used", "Memory used", SensorCategory.Memory, "bytes");
        Add("ram.used_pct", "Memory used (%)", SensorCategory.Memory, "%");
        Add("fps", "Frames per second", SensorCategory.Gaming, "fps");
        Add("fps.1pct_low", "FPS: 1% low", SensorCategory.Gaming, "fps");
        Add("frametime", "Frame time", SensorCategory.Gaming, "ms");
        Add("fps.app", "Game or app", SensorCategory.Gaming);
        Add("now", "Current date and time", SensorCategory.TimeAndDate);
        foreach (var (prefix, title) in new[] { ("five_hour", "five-hour limit"), ("seven_day", "weekly limit"), ("seven_day_model", "weekly model limit") })
        {
            Add($"claude.{prefix}.pct", $"Claude Code: {title} used", SensorCategory.ClaudeCode, "%");
            Add($"claude.{prefix}.resets_at", $"Claude Code: {title} resets", SensorCategory.ClaudeCode);
        }
        Add("claude.seven_day_model.name", "Claude Code: weekly limit model", SensorCategory.ClaudeCode);
        Add("claude.context.pct", "Claude Code: context used", SensorCategory.ClaudeCode, "%");
        foreach (var (prefix, title) in new[] { ("primary", "primary limit"), ("secondary", "secondary limit") })
        {
            Add($"codex.{prefix}.pct", $"Codex: {title} used", SensorCategory.Codex, "%");
            Add($"codex.{prefix}.resets_at", $"Codex: {title} resets", SensorCategory.Codex);
            Add($"codex.{prefix}.window_min", $"Codex: {title} window", SensorCategory.Codex, "min");
        }
        foreach (var (prefix, title, category) in new[] { ("claude", "Claude Code", SensorCategory.ClaudeCode), ("codex", "Codex", SensorCategory.Codex) })
        {
            Add(prefix + ".sessions.total", title + ": total sessions", category);
            Add(prefix + ".sessions.busy", title + ": busy sessions", category);
            Add(prefix + ".sessions.idle", title + ": idle sessions", category);
            Add(prefix + ".updated_at", title + ": last updated", category);
        }
        return names;
    }
}
