using Marquee.Core.Themes.Parsing;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// A value the editor writes into layout.yaml, as the YAML text of the value alone: `120`, `{ size: 30 }`, or a block
/// mapping or list written from column 0 (`size: 30\nweight: bold`). LayoutDocument places its lines and indents them
/// to fit where it goes, with the file's line endings, and checks the file then reads back the same value as this.
sealed record YamlFragment(IReadOnlyList<string> Lines, YamlTree Tree, bool IsBlockCollection)
{
    public bool IsMultiLine => Lines.Count > 1;

    /// A `|` or `>` text, whose header line can hold a comment and whose last line ends it.
    public bool IsBlockScalar { get; init; }

    /// The fragment's lines, the first at `column` when `indentFirst` and the rest always, joined with `eol`. An empty
    /// line gets no spaces.
    public string Indented(int column, bool indentFirst, string eol)
    {
        var pad = new string(' ', column);
        return string.Join(eol, Lines.Select((line, i) => line.Length == 0 || (i == 0 && !indentFirst) ? line : pad + line));
    }

    /// Throws SpliceException for text that isn't exactly one value, or that uses an anchor or alias.
    public static YamlFragment Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        // Only YAML's own white space: .NET's Trim() would also take a no-break space, which YAML reads as text.
        var text = yaml.Replace("\r\n", "\n").Trim(' ', '\t', '\r', '\n');
        if (text.Length == 0) throw new SpliceException("The new value is empty. Write '' for empty text.");
        if (LayoutScalar.LoneSurrogate(text) >= 0) throw new SpliceException(LayoutScalar.LoneSurrogateReason("The new value"));

        var stream = new YamlStream();
        try
        {
            // Read as it will sit in the file: followed by a line break.
            stream.Load(new DepthLimitedParser(new Parser(new StringReader(text + "\n")), Yaml.MaxDepth));
        }
        catch (Exception ex) when (ex is YamlException or ThemeException or InvalidOperationException or ArgumentException)
        {
            // YamlDotNet throws the last two, without a position, for some malformed text (`- :`, a bad tag).
            throw new SpliceException($"The new value isn't valid YAML: {Reason(ex)}");
        }

        if (stream.Documents.Count != 1) throw new SpliceException("The new value must be one YAML value.");
        var root = stream.Documents[0].RootNode;
        var tree = YamlTree.From(root)!;
        if (tree.UsesAnchors())
            throw new SpliceException("The new value can't use an anchor (&) or alias (*).");

        var block = root is YamlMappingNode { Style: MappingStyle.Block } or YamlSequenceNode { Style: SequenceStyle.Block };
        return new YamlFragment(text.Split('\n'), tree, block)
        {
            IsBlockScalar = root is YamlScalarNode { Style: ScalarStyle.Literal or ScalarStyle.Folded },
        };
    }

    /// A YamlDotNet failure's message for the user, without the `(Parameter 'parser')` an ArgumentException adds.
    public static string Reason(Exception ex) =>
        ex is ArgumentException { ParamName: { } name } ? ex.Message.Replace($" (Parameter '{name}')", "") : ex.Message;
}
