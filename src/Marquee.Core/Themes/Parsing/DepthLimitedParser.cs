using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Marquee.Core.Themes.Parsing;

/// Passes YamlDotNet's parser events through, counting how deeply lists and mappings nest, and throws a ThemeException
/// with the line of the first one that goes deeper than `maxDepth`. YamlStream builds its node tree by recursion, a few
/// calls per level, so 10 KB of `[` overflows the stack, and a stack overflow ends the process whatever catches it:
/// stopping at the event, before the tree goes any deeper, makes it an error like any other.
sealed class DepthLimitedParser(IParser inner, int maxDepth) : IParser
{
    int _depth;

    public ParsingEvent? Current => inner.Current;

    public bool MoveNext()
    {
        if (!inner.MoveNext()) return false;

        var current = inner.Current;
        if (current is MappingStart or SequenceStart)
        {
            if (++_depth > maxDepth)
            {
                throw new ThemeException(
                    $"Lists and mappings are nested more than {maxDepth} levels deep", (int)current.Start.Line);
            }
        }
        else if (current is MappingEnd or SequenceEnd)
        {
            _depth--;
        }

        return true;
    }
}
