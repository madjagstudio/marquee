using Marquee.Core.Expressions;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Host;

/// `marquee sensors`: reads the hardware source twice (load percentages need two samples) and the agent providers
/// once, and prints every key, so theme authors can see what's available and the providers can be checked on a real PC.
/// Claude Code's limits are read by running `claude -p /usage` (skipped with --no-claude-usage, or with
/// `providers: { claude_usage: off }` in config.yaml). The hardware source is --sensors, else config.yaml's. The context
/// percentage, which only the status line carries, and custom values come from other programs while `marquee run` is
/// going, so they aren't listed.
static class SensorsCommand
{
    /// Null means the options were wrong and the caller should print usage.
    public static int? Run(string[] options)
    {
        string? source = null;
        bool? claudeUsage = null;
        string? configFile = null;
        for (var i = 0; i < options.Length; i++)
        {
            var value = i + 1 < options.Length ? options[i + 1] : null;
            switch (options[i])
            {
                case "--sensors" when SensorSourceNames.IsValid(value):
                    source = value!;
                    i++;
                    break;
                case "--no-claude-usage":
                    claudeUsage = false;
                    break;
                case "--config" when value is not null:
                    configFile = value;
                    i++;
                    break;
                default:
                    return null;
            }
        }

        var log = new ConsoleLog();
        var config = ConfigOption.Load(configFile, log);
        source ??= config.Sensors ?? SensorSourceNames.Default;
        var store = new SensorStore();
        using (var hardware = SensorSources.Create(source, log))
        {
            if (hardware is not null && !TryPoll(hardware, store, log, twice: true))
                return 1;
        }

        foreach (var agent in AgentSources.Create(AgentSources.Enabled(claudeUsage: claudeUsage ?? config.ClaudeUsage), log))
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

        if (keys.Count == 0)
            log.Warn(Strings.NoReadings);
        return keys.Count > 0 ? 0 : 1;
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
