using System.Globalization;
using Marquee.Core.Expressions;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Host;
using Marquee.Providers.Hardware;

/// `marquee sensors`: reads the hardware source twice (load percentages need two samples) and the agent providers
/// once, and prints every key, so theme authors can see what's available and the providers can be checked on a real PC.
/// Claude Code's limits are read by running `claude -p /usage` (skipped with --no-claude-usage, or with
/// `providers: { claude_usage: off }` in config.yaml). The hardware source is --sensors, else config.yaml's. The context
/// percentage, which only the status line carries, and custom values come from other programs while `marquee run` is
/// going, so they aren't listed. Nor is FPS: it needs a game in front, presenting frames for a while, and an elevated
/// Marquee, which a one-off listing typed into a terminal doesn't have. --raw then lists the hardware source's own
/// readings, so the labels the key map looks for can be checked against what a PC reports.
static class SensorsCommand
{
    /// The command line after `sensors`.
    internal sealed record Options
    {
        public string? Sensors { get; init; }

        /// False for `--no-claude-usage`.
        public bool? ClaudeUsage { get; init; }

        /// `--config <file>`: read this file instead of %LOCALAPPDATA%\Marquee\config.yaml.
        public string? ConfigFile { get; init; }

        /// `--raw`: after the keys, list the hardware source's readings as it named them.
        public bool Raw { get; init; }
    }

    /// Null means the options were wrong and the caller should print usage.
    public static int? Run(string[] args) => Parse(args) is { } options ? Run(options) : null;

    /// Null if a flag or its value is wrong.
    internal static Options? Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--sensors" when SensorSourceNames.IsValid(value): options = options with { Sensors = value }; i++; break;
                case "--no-claude-usage": options = options with { ClaudeUsage = false }; break;
                case "--raw": options = options with { Raw = true }; break;
                case "--config" when value is not null: options = options with { ConfigFile = value }; i++; break;
                default: return null;
            }
        }

        return options;
    }

    static int Run(Options options)
    {
        var log = new ConsoleLog();
        var config = ConfigOption.Load(options.ConfigFile, log);
        var source = options.Sensors ?? config.Sensors ?? SensorSourceNames.Default;
        var store = new SensorStore();
        if (PollHardware(SensorSources.Create(source, log), store, log) is not { } hardware)
            return 1;

        foreach (var agent in AgentSources.Create(AgentSources.Enabled(claudeUsage: options.ClaudeUsage ?? config.ClaudeUsage), log))
        {
            using (agent)
                TryPoll(agent, store, log, twice: false);
        }

        var snapshot = store.Snapshot();
        var keys = snapshot.Keys.ToList();
        foreach (var key in keys)
        {
            snapshot.TryGet(key, out var value);
            Console.WriteLine($"{key,-28} {Formatting.Format(value)}");
        }

        if (options.Raw)
            WriteRaw(hardware, log);

        if (keys.Count == 0)
            log.Warn(Strings.NoReadings);
        return keys.Count > 0 ? 0 : 1;
    }

    /// The hardware source's name and its last raw readings, both null without a source (`sensors: none`).
    internal sealed record HardwareListing(string? Source, IReadOnlyList<RawReading>? Readings);

    /// Polls the hardware source into the store, takes what --raw lists, and closes the source; null if the poll
    /// failed. The name and readings are taken while the source is open: a closed one needn't keep them.
    internal static HardwareListing? PollHardware(ISensorProvider? hardware, SensorStore store, ILog log, bool twice = true)
    {
        using (hardware)
        {
            if (hardware is not null && !TryPoll(hardware, store, log, twice))
                return null;
            return new HardwareListing(hardware?.Name, (hardware as IRawReadings)?.LastReadings);
        }
    }

    /// The raw listing under a heading naming the source, or a warning when there is no hardware source.
    internal static void WriteRaw(HardwareListing hardware, ILog log)
    {
        if (hardware is not { Source: { } source, Readings: { } readings })
        {
            log.Warn(Strings.NoRawReadings);
            return;
        }

        Console.WriteLine();
        Console.WriteLine(Strings.RawReadings(source));
        foreach (var line in RawLines(readings))
            Console.WriteLine(line);
    }

    /// One line per reading: hardware, type, label, value with its unit, and device; sorted by hardware, then device in
    /// the source's order, which is the order the key map prefers devices in when it has no better clue, then type and
    /// label.
    internal static IEnumerable<string> RawLines(IReadOnlyList<RawReading> readings)
    {
        var devices = readings.Select(r => r.Device).Distinct(StringComparer.Ordinal).ToList();
        return readings
            .OrderBy(r => r.Hardware)
            .ThenBy(r => devices.IndexOf(r.Device))
            .ThenBy(r => r.Type, StringComparer.Ordinal)
            .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var value = r.Value.ToString("0.####", CultureInfo.InvariantCulture) + (r.Unit.Length > 0 ? " " + r.Unit : "");
                return $"{r.Hardware,-6} {r.Type,-11} {r.Label,-32} {value,-14} {r.Device}".TrimEnd();
            });
    }

    static bool TryPoll(ISensorProvider provider, SensorStore store, ILog log, bool twice)
    {
        try
        {
            provider.Poll(store);
            if (twice)
            {
                Thread.Sleep(1000);
                provider.Poll(store);
            }

            return true;
        }
        catch (Exception ex)
        {
            log.Error(Strings.SourceFailed(provider.Name, ex.Message));
            return false;
        }
    }
}
