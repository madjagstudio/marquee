namespace Marquee.Core.Config;

/// A config.yaml that can't be used. Line is 1-based within config.yaml, or 0 when the problem isn't on one line;
/// then the message is the reason alone.
public sealed class ConfigException(string reason, int line = 0)
    : Exception(line > 0 ? $"{ConfigParser.FileName} line {line}: {reason}" : reason)
{
    public string Reason { get; } = reason;
    public int Line { get; } = line;
}
