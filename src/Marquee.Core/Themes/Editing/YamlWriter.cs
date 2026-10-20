using Marquee.Core.Config;

namespace Marquee.Core.Themes.Editing;

/// Writes a YamlValue as YAML in the style of the example themes, for a new widget or value that LayoutDocument then
/// places (InsertItem, ReplaceValue, AddKey):
/// - a single value plain where YAML reads it back as the same text, and in double quotes otherwise (`"#FF5A36"`,
///   `"{0:0}°C"`);
/// - lists and mappings on one line, `[0, 0]` and `{ size: 30 }`;
/// - except a list of mappings, such as `when` rules, a graph's `series` or a group's `children`, which goes in block
///   style under its key, an item to a line. An item is a `{ … }` one-liner unless it holds such a list itself.
///
/// The text is written from column 0 with `\n` line breaks; LayoutDocument indents it and uses the file's line endings.
public static class YamlWriter
{
    /// A single value: plain where YAML reads it back as the same text there (in a block mapping or list, or with
    /// `inFlow` inside `{ }` or `[ ]`), else double-quoted, with escapes for line breaks and control characters.
    public static string Scalar(string value, bool inFlow = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        return LayoutScalar.IsPlain(value, inFlow) ? value : YamlScalar.DoubleQuoted(value);
    }

    /// The value on one line: `{ type: text, at: [0, 0] }`, `[1, 2]`, or a single value. An empty mapping is `{ }`
    /// and an empty list `[]`.
    public static string Flow(YamlValue value) => value switch
    {
        YamlText text => Scalar(text.Value, inFlow: true),
        YamlList { Items.Count: 0 } => "[]",
        YamlList list => $"[{string.Join(", ", list.Items.Select(Flow))}]",
        YamlMap { Entries.Count: 0 } => "{ }",
        YamlMap map => $"{{ {FlowEntries(map)} }}",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// A mapping's entries as they go inside its braces: `type: text, at: [0, 0]`.
    internal static string FlowEntries(YamlMap map) =>
        string.Join(", ", map.Entries.Select(e => $"{Scalar(e.Key, inFlow: true)}: {Flow(e.Value)}"));

    /// The mapping in block style, an entry to a line. Each value is on one line after its key, but a list of mappings
    /// (and a mapping holding one) goes on the lines below, `step` columns in, as the example themes write `when`.
    public static string Block(YamlMap map, int step = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(step, 1);
        return string.Join("\n", BlockLines(map, step));
    }

    /// The value as the example themes write it: in block style when it holds a list of mappings, else on one line,
    /// where a single value is quoted as it must be inside `{ }` or `[ ]`, so it reads the same wherever it goes.
    public static string Write(YamlValue value, int step = 2) => value switch
    {
        _ when !NeedsBlock(value) => Flow(value),
        YamlMap map => Block(map, step),
        YamlList list => string.Join("\n", ListLines(list, step)),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// True when the value can't be written on one line in the theme's style: it is, or holds, a list with a mapping
    /// in it.
    public static bool NeedsBlock(YamlValue value) => value switch
    {
        YamlList list => list.Items.Any(item => item is YamlMap || NeedsBlock(item)),
        YamlMap map => map.Entries.Any(e => NeedsBlock(e.Value)),
        _ => false,
    };

    static IEnumerable<string> BlockLines(YamlMap map, int step)
    {
        var pad = new string(' ', step);
        foreach (var (key, value) in map.Entries)
        {
            var keyText = Scalar(key);
            if (!NeedsBlock(value))
            {
                yield return $"{keyText}: {Flow(value)}";
                continue;
            }

            yield return keyText + ":";
            var lines = value is YamlMap inner ? BlockLines(inner, step) : ListLines((YamlList)value, step);
            foreach (var line in lines) yield return pad + line;
        }
    }

    /// A block list, `- ` before each item: a one-liner, or an item that needs block style on its lines, the later
    /// ones two columns in to line up after the dash.
    static IEnumerable<string> ListLines(YamlList list, int step)
    {
        foreach (var item in list.Items)
        {
            if (!NeedsBlock(item))
            {
                yield return "- " + Flow(item);
                continue;
            }

            var lines = item is YamlMap map ? BlockLines(map, step) : ListLines((YamlList)item, step);
            var first = true;
            foreach (var line in lines)
            {
                yield return (first ? "- " : "  ") + line;
                first = false;
            }
        }
    }
}
