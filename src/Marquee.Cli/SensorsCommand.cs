using Marquee.Core.Expressions;
using Marquee.Core.Sensors;

/// `marquee sensors`: reads the hardware source twice (load percentages need two samples) and prints every key,
/// so theme authors can see what's available and the provider can be checked on a real PC.
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
                case "--sensors" when SensorSources.IsValid(value) && value != "none":
                    source = value!;
                    i++;
                    break;
                default:
                    return null;
            }
        }

        var log = new ConsoleLog();
        using var provider = SensorSources.Create(source, log)!;
        var store = new SensorStore();
        try
        {
            provider.Poll(store);
            Thread.Sleep(1000);
            provider.Poll(store);
        }
        catch (Exception ex)
        {
            log.Error($"{provider.Name} failed: {ex.Message}");
            return 1;
        }

        var snapshot = store.Snapshot();
        var keys = snapshot.Keys.ToList();
        foreach (var key in keys)
        {
            snapshot.TryGet(key, out var value);
            Console.WriteLine($"{key,-24} {Formatting.Format(value)}");
        }

        if (keys.Count == 0)
            log.Warn($"{provider.Name} returned no readings.");
        return keys.Count > 0 ? 0 : 1;
    }
}
