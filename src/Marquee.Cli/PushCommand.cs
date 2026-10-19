using Marquee.Core.Config;
using Marquee.Providers.Custom;

/// `marquee push <key> <value> [--ttl 30s] [--port N] [--config <file>]`: sets a custom value in a running Marquee. The
/// "custom." prefix may be left off the key. Without --port, it uses config.yaml's http port, as `marquee run` does.
static class PushCommand
{
    /// Null means the arguments were wrong and the caller should print usage.
    public static int? Run(string key, string value, string[] options)
    {
        TimeSpan? ttl = null;
        int? port = null;
        string? configFile = null;
        for (var i = 0; i < options.Length; i++)
        {
            var next = i + 1 < options.Length ? options[i + 1] : null;
            switch (options[i])
            {
                case "--ttl" when DurationText.TryParse(next, out var t): ttl = t; i++; break;
                case "--port" when HttpPort.TryParse(next, out var p): port = p; i++; break;
                case "--config" when next is not null: configFile = next; i++; break;
                default: return null;
            }
        }

        if (!key.StartsWith(CustomValues.Prefix, StringComparison.Ordinal)) key = CustomValues.Prefix + key;
        if (!CustomValues.IsValidKey(key))
        {
            Console.Error.WriteLine(Strings.InvalidCustomKey(key, CustomValues.MaxKeyLength));
            return 1;
        }

        if (port is null)
        {
            var config = ConfigOption.Load(configFile, new ConsoleLog());
            if (!config.HttpEnabled)
            {
                Console.Error.WriteLine(Strings.HttpOffInConfig(ConfigOption.Path(configFile)));
                return 1;
            }

            port = config.HttpPort ?? CustomValuesServer.DefaultPort;
        }

        if (CustomValuesClient.Push(port.Value, key, value, ttl) is { } error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        return 0;
    }
}

static class HttpPort
{
    public static bool TryParse(string? text, out int port) =>
        int.TryParse(text, out port) && port is >= MarqueeConfig.MinHttpPort and <= MarqueeConfig.MaxHttpPort;
}
