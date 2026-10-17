namespace Marquee.Core.Expressions;

/// A malformed expression or template. Position is the 0-based character index of the problem.
public sealed class ExpressionException(string reason, int position)
    : Exception($"{reason} at character {position + 1}")
{
    public string Reason { get; } = reason;
    public int Position { get; } = position;
}
