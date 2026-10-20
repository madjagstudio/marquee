using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// A list whose length the format limits, such as a graph's series or a donut's slices, read entry by entry.
static class ListParser
{
    /// The entries of the list `what`, between `min` and `max` of them, each read by `parse`. When collecting, every entry
    /// is checked before the first mistake is thrown again for the widget, which leaves it out; ParseErrors already holds
    /// that mistake, so it isn't reported twice.
    public static IReadOnlyList<T> Entries<T>(YamlNode node, string what, int min, int max, ParseErrors errors, Func<YamlNode, T> parse)
    {
        var items = Yaml.List(node, what);
        if (items.Count < min || items.Count > max)
            throw Yaml.Error(node, $"{what} must have between {min} and {max} entries, not {items.Count}");

        var entries = new List<T>(items.Count);
        ThemeException? first = null;
        foreach (var item in items)
        {
            try
            {
                entries.Add(parse(item));
            }
            catch (ThemeException ex) when (errors.Absorb(ex))
            {
                first ??= ex;
            }
        }

        return first is null ? entries : throw first;
    }
}
