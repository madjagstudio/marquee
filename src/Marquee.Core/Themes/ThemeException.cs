namespace Marquee.Core.Themes;

/// A theme that can't be used. Line is 1-based within layout.yaml, or 0 when the problem isn't on one line
/// (a missing folder, an empty file); then the message is the reason alone.
public sealed class ThemeException(string reason, int line = 0)
    : Exception(line > 0 ? $"layout.yaml line {line}: {reason}" : reason)
{
    public string Reason { get; } = reason;
    public int Line { get; } = line;
}
