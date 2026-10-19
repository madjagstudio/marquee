using Marquee.Providers.Custom;

/// `marquee push <key> <value> [--ttl 30s] [--port N]`: sets a custom value in a running Marquee. The "custom." prefix
/// may be left off the key.
static class PushCommand
{
    /// Null means the arguments were wrong and the caller should print usage.
    public static int? Run(string key, string value, string[] options)
    {
        TimeSpan? ttl = null;
        var port = CustomValuesServer.DefaultPort;
        for (var i = 0; i < options.Length; i++)
        {
            var next = i + 1 < options.Length ? options[i + 1] : null;
            switch (options[i])
            {
                case "--ttl" when DurationText.TryParse(next, out var t): ttl = t; i++; break;
                case "--port" when HttpPort.TryParse(next, out var p): port = p; i++; break;
                default: return null;
            }
        }

        if (!key.StartsWith(CustomValues.Prefix, StringComparison.Ordinal)) key = CustomValues.Prefix + key;
        if (!CustomValues.IsValidKey(key))
        {
            Console.Error.WriteLine(Strings.InvalidCustomKey(key, CustomValues.MaxKeyLength));
            return 1;
        }

        if (CustomValuesClient.Push(port, key, value, ttl) is { } error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        return 0;
    }
}

static class HttpPort
{
    public static bool TryParse(string? text, out int port) => int.TryParse(text, out port) && port is >= 1024 and <= 65535;
}
