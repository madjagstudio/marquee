using Marquee.Core.Sensors;

namespace Marquee.Editor.Model.Values;

/// Believable stand-in values, for keys nothing else has: a theme then looks as it will on a working PC (52° on a CPU,
/// 120 fps) rather than full of dashes. Keys are matched in the table's order, exact names before patterns, where `*`
/// stands for any run of characters; a key matching nothing reads 50. Times are Unix seconds near `now`, as the
/// providers write them.
public static class Samples
{
    const double GiB = 1024.0 * 1024 * 1024;

    /// What a key nothing in the table matches reads: the middle of the default slider.
    public const double Unknown = 50;

    static readonly (string Pattern, Func<DateTimeOffset, SensorValue> Value)[] Table =
    [
        ("now", now => Seconds(now, TimeSpan.Zero)),
        // Values pushed with `marquee push` can be anything; the middle of the default slider, whatever the name.
        ("custom.*", _ => SensorValue.Of(Unknown)),

        ("cpu.temp", _ => SensorValue.Of(52)),
        ("gpu.temp", _ => SensorValue.Of(58)),
        ("*.temp", _ => SensorValue.Of(45)),
        ("ram.used_pct", _ => SensorValue.Of(45)),
        ("*.load", _ => SensorValue.Of(30)),
        ("cpu.clock", _ => SensorValue.Of(4500)),
        ("gpu.clock", _ => SensorValue.Of(2100)),
        ("*.clock", _ => SensorValue.Of(3000)),
        ("cpu.power", _ => SensorValue.Of(65)),
        ("gpu.power", _ => SensorValue.Of(180)),
        ("*.power", _ => SensorValue.Of(50)),
        ("cpu.voltage", _ => SensorValue.Of(1.2)),
        ("gpu.voltage", _ => SensorValue.Of(0.95)),
        ("*.voltage", _ => SensorValue.Of(1.2)),
        ("gpu.fan", _ => SensorValue.Of(1400)),
        ("fan.*", _ => SensorValue.Of(1100)),
        ("ram.used", _ => SensorValue.Of(16 * GiB)),
        ("gpu.vram_used", _ => SensorValue.Of(6 * GiB)),

        ("fps", _ => SensorValue.Of(120)),
        ("fps.1pct_low", _ => SensorValue.Of(96)),
        ("frametime", _ => SensorValue.Of(8.3)),
        ("fps.app", _ => SensorValue.Of("game")),

        ("claude.five_hour.pct", _ => SensorValue.Of(42)),
        ("claude.five_hour.resets_at", now => Seconds(now, new TimeSpan(2, 14, 0))),
        ("claude.seven_day.pct", _ => SensorValue.Of(18)),
        ("claude.seven_day.resets_at", now => Seconds(now, new TimeSpan(3, 6, 0, 0))),
        ("claude.seven_day_model.pct", _ => SensorValue.Of(25)),
        ("claude.seven_day_model.resets_at", now => Seconds(now, new TimeSpan(3, 6, 0, 0))),
        ("claude.seven_day_model.name", _ => SensorValue.Of("Opus")),
        ("claude.context.pct", _ => SensorValue.Of(35)),
        ("claude.sessions.total", _ => SensorValue.Of(3)),
        ("claude.sessions.busy", _ => SensorValue.Of(1)),
        ("claude.sessions.idle", _ => SensorValue.Of(2)),

        ("codex.primary.pct", _ => SensorValue.Of(30)),
        ("codex.primary.resets_at", now => Seconds(now, new TimeSpan(3, 20, 0))),
        ("codex.primary.window_min", _ => SensorValue.Of(300)),
        ("codex.secondary.pct", _ => SensorValue.Of(12)),
        ("codex.secondary.resets_at", now => Seconds(now, new TimeSpan(5, 2, 0, 0))),
        ("codex.secondary.window_min", _ => SensorValue.Of(10080)),
        ("codex.sessions.total", _ => SensorValue.Of(2)),
        ("codex.sessions.busy", _ => SensorValue.Of(1)),
        ("codex.sessions.idle", _ => SensorValue.Of(1)),

        ("*.updated_at", now => Seconds(now, TimeSpan.FromSeconds(-30))),
        ("*.resets_at", now => Seconds(now, TimeSpan.FromHours(1))),
        ("*.pct", _ => SensorValue.Of(40)),
    ];

    /// The keys the table names outright, in its order, `now` aside: what a key picker can offer beside the theme's own
    /// keys. Patterns aren't keys, and are left out.
    public static IReadOnlyList<string> Keys { get; } =
        Table.Select(entry => entry.Pattern).Where(pattern => !pattern.Contains('*') && pattern != "now").ToList();

    /// True when the table has the key, by name or by pattern; other keys read Unknown.
    public static bool Knows(string key) => Find(key) is not null;

    /// The key's sample at `now`.
    public static SensorValue ValueFor(string key, DateTimeOffset now) => Find(key)?.Invoke(now) ?? SensorValue.Of(Unknown);

    /// A graph's worth of sample history for the key, oldest first, ending on its sample: a slow wave a few percent
    /// either side, the same every time for a key, so a graph looks alive and stays still. Empty for a text key.
    public static IReadOnlyList<double> HistoryFor(string key, DateTimeOffset now, int points)
    {
        if (ValueFor(key, now).Number is not { } value || points <= 0) return [];
        var amplitude = Math.Abs(value) * 0.06;
        var phase = Phase(key);
        var end = Math.Sin(phase + (points - 1) * 0.21);
        var history = new double[points];
        for (var i = 0; i < points; i++)
            history[i] = value + amplitude * (Math.Sin(phase + i * 0.21) - end);
        return history;
    }

    static Func<DateTimeOffset, SensorValue>? Find(string key)
    {
        foreach (var (pattern, value) in Table)
            if (Matches(pattern, key))
                return value;
        return null;
    }

    /// `*` matches any run of characters, dots included; everything else matches itself.
    internal static bool Matches(string pattern, string key)
    {
        var parts = pattern.Split('*');
        if (parts.Length == 1) return string.Equals(pattern, key, StringComparison.Ordinal);
        if (!key.StartsWith(parts[0], StringComparison.Ordinal) || !key.EndsWith(parts[^1], StringComparison.Ordinal)) return false;
        if (key.Length < parts[0].Length + parts[^1].Length) return false;

        var at = parts[0].Length;
        var end = key.Length - parts[^1].Length;
        for (var i = 1; i < parts.Length - 1; i++)
        {
            var found = key.IndexOf(parts[i], at, end - at, StringComparison.Ordinal);
            if (found < 0) return false;
            at = found + parts[i].Length;
        }

        return true;
    }

    static SensorValue Seconds(DateTimeOffset now, TimeSpan from) => SensorValue.Of((now + from).ToUnixTimeSeconds());

    /// A phase from the key's characters, so each key's wave starts somewhere of its own; string.GetHashCode changes
    /// from run to run.
    static double Phase(string key)
    {
        var hash = 2166136261u;
        foreach (var c in key) hash = (hash ^ c) * 16777619u;
        return hash % 628 / 100.0;
    }
}
