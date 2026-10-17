using Marquee.Core.Expressions;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;

/// `marquee sensors`: reads the hardware source twice (load percentages need two samples) and the agent providers
/// once, and prints every key, so theme authors can see what's available and the providers can be checked on a real PC.
/// Claude Code's limits and custom values come from other programs while `marquee run` is going, so they aren't listed.
static class SensorsCommand
{
    /// Null means the options were wrong and the caller should print usage.
    public static int? Run(string[] options)
    {
        var source = SensorSources.Default;
        for (var i = 0; i < options.Length; i++)
        {
            var value = i + 1 < options.Length ? options[i + 1] : null;
            switch (options[i])
            {
                case "--sensors" when SensorSources.IsValid(value):
                    source = value!;
                    i++;
                    break;
                default:
                    return null;
            }
        }

        var log = new ConsoleLog();
        var store = new SensorStore();
        using (var hardware = SensorSources.Create(source, log))
        {
            if (hardware is not null && !TryPoll(hardware, store, log, twice: true))
                return 1;
        }

        foreach (var agent in AgentSources.Create(log))
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
            log.Warn("No provider returned any readings.");
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
            log.Error($"{provider.Name} failed: {ex.Message}");
            return false;
        }
    }
}
