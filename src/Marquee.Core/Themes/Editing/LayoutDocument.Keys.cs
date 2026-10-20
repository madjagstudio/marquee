using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// Adding and removing a mapping's keys.
public sealed partial class LayoutDocument
{
    /// Adds `key`, with the value `valueYaml` (written as for ReplaceValue), as the last entry of the mapping at
    /// `mapping`: inside the braces of a `{ }` mapping, else on its own line after the mapping's last entry, at its
    /// indentation. An empty value (`style:`) becomes a mapping holding the key, and an empty file one at the top.
    public LayoutDocument AddKey(YamlPath mapping, string key, string valueYaml)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (LayoutScalar.LoneSurrogate(key) >= 0) throw new LayoutEditException(LayoutScalar.LoneSurrogateReason("The key"));
        var fragment = Fragment(valueYaml);
        YamlTree Added(YamlTree? node) =>
            (node as YamlTreeMap ?? YamlTreeMap.Empty).With(key, fragment.Tree);

        if (_root is null)
        {
            if (!mapping.IsRoot) throw new LayoutEditException($"{Name(mapping)} isn't in the file.");
            return Edit(mapping, () => (
                [YamlSplice.AddToMapping(Text, null, Entry(LayoutScalar.Key(key, false), fragment), Eol, atEndOfFile: true)],
                _ => Added(null)));
        }

        var at = Locate(mapping);
        CheckEditable(at, inside: false);
        var inFlow = at.InFlow || YamlSplice.IsFlow(at.Node);
        if (inFlow && fragment.IsMultiLine)
            throw new LayoutEditException($"{Name(mapping)} is a {{ }} mapping, so the value of {key} must be written on one line.", at.Line);

        var keyText = LayoutScalar.Key(key, inFlow);
        Splice splice;
        switch (at.Node)
        {
            case YamlMappingNode map when map.Children.Any(e => e.Key is YamlScalarNode { Value: var k } && k == key):
                throw new LayoutEditException($"{Name(mapping)} already has {key}.", at.Line);
            case YamlMappingNode map:
                splice = YamlSplice.AddToMapping(Text, map, Entry(keyText, fragment), Eol, atEndOfFile: false);
                break;
            case YamlScalarNode empty when YamlSplice.IsEmpty(empty) && inFlow:
                splice = YamlSplice.Fill(Text, empty, $"{{ {keyText}: {fragment.Lines[0]} }}");
                break;
            case YamlScalarNode empty when YamlSplice.IsEmpty(empty) && at.Key is { } parentKey:
                // `style:` with nothing after it: the key goes on a new line under it, past any comment.
                var indent = Pad(Column((int)parentKey.Start.Index) + Step);
                var lines = indent + Entry(keyText, fragment).Replace(Eol, Eol + indent);
                var lineEnd = YamlSplice.LineBreakAfter(Text, (int)empty.Start.Index);
                splice = Splice.Insert(lineEnd, Eol + lines + (lineEnd == Text.Length ? Eol : ""));
                break;
            default:
                throw new LayoutEditException($"{Name(mapping)} isn't a mapping of keys to values, so it can't take {key}.", at.Line);
        }

        return Edit(mapping, () => ([splice], tree => YamlTree.Change(tree, mapping, Added)));
    }

    /// `key: value` for AddToMapping, which indents every line by the mapping's indentation: a block value on the
    /// lines below, one step in, and the later lines of a `|` text as written.
    string Entry(string keyText, YamlFragment fragment) => fragment.IsBlockCollection
        ? $"{keyText}:{Eol}{fragment.Indented(Step, true, Eol)}"
        : $"{keyText}: {fragment.Indented(0, false, Eol)}";

    static string Pad(int columns) => new(' ', columns);

    /// Removes the key at `path` and its value. From a `{ }` mapping that is the entry and its comma; removing the last
    /// one leaves `{ }`. From a block mapping it is the key's lines, with what belongs to them:
    /// - the comment lines directly above the key, at its indentation (they describe it), unless they start the file
    ///   (those describe the file);
    /// - the value's lines, and the comment lines among and right after them that are indented deeper than the key
    ///   (they are inside the value);
    /// - a blank line, when the key had one above and below it, so its neighbours keep one blank line between them.
    ///
    /// Removing the only key of a block mapping leaves `{ }`, so the mapping stays a mapping (`style: { }`).
    public LayoutDocument RemoveKey(YamlPath path)
    {
        if (path.Last is not { IsKey: true } last)
            throw new LayoutEditException($"{Name(path)} isn't a mapping's key, so RemoveKey can't remove it.");

        var at = Locate(path);
        CheckEditable(at, inside: true);
        var map = (YamlMappingNode)at.Parent!;
        var entries = map.Children.ToList();
        var index = entries.FindIndex(e => ReferenceEquals(e.Key, at.Key));

        return Edit(path, () => (
            [YamlSplice.IsFlow(map) ? RemoveFromFlow(map, entries, index) : RemoveFromBlock(at, entries, index)],
            tree => YamlTree.Change(tree, path.Parent!, m => ((YamlTreeMap)m!).Without(last.Key!))));
    }

    Splice RemoveFromFlow(YamlMappingNode map, List<KeyValuePair<YamlNode, YamlNode>> entries, int index)
    {
        var (key, value) = entries[index];
        if (entries.Count == 1)
        {
            var open = YamlSplice.ContentStart(Text, map) + 1;
            var close = YamlSplice.Closing(Text, YamlSplice.EntryEnd(Text, key, value));
            return new Splice(open, close - open, " ");
        }

        // Up to the next key, taking the comma; the last entry takes the comma before it instead.
        if (index < entries.Count - 1)
        {
            var next = (int)entries[index + 1].Key.Start.Index;
            return new Splice((int)key.Start.Index, next - (int)key.Start.Index, "");
        }

        var previous = YamlSplice.EntryEnd(Text, entries[index - 1].Key, entries[index - 1].Value);
        return new Splice(previous, YamlSplice.EntryEnd(Text, key, value) - previous, "");
    }

    Splice RemoveFromBlock(Located at, List<KeyValuePair<YamlNode, YamlNode>> entries, int index)
    {
        var (key, value) = entries[index];
        if (entries.Count == 1 && at.Chain.Count > 2) return EmptyMapping(at.Path.Parent!);

        var keyStart = (int)key.Start.Index;
        var start = YamlSplice.LineStart(Text, keyStart);
        if (Text.AsSpan(start, keyStart - start).Trim(' ').Length > 0)
        {
            // The first key of a list item, on the dash's line (`- type: text`): the next key moves up beside the dash.
            var next = (int)entries[index + 1].Key.Start.Index;
            return new Splice(keyStart, next - keyStart, "");
        }

        var column = keyStart - start;
        var end = YamlSplice.LineStartAfter(Text, ContentEnd(keyStart, YamlSplice.EntryEnd(Text, key, value)));

        var above = start;
        while (above > 0 && IsComment(YamlSplice.LineStart(Text, above - 1), c => c == column))
            above = YamlSplice.LineStart(Text, above - 1);
        if (above > 0) start = above;

        while (end < Text.Length && IsComment(end, c => c > column)) end = YamlSplice.NextLine(Text, end);

        var blankAbove = start == 0 || IsBlank(YamlSplice.LineStart(Text, start - 1));
        if (blankAbove && end < Text.Length && IsBlank(end)) end = YamlSplice.NextLine(Text, end);
        else if (start > 0 && end == Text.Length && IsBlank(YamlSplice.LineStart(Text, start - 1)))
            start = YamlSplice.LineStart(Text, start - 1);

        return new Splice(start, end - start, "");
    }

    /// Removing the only key of a block mapping: the mapping becomes `{ }`, after its key (keeping a comment on the
    /// key's line) or on its list item's line. The removed key's own comment on its line goes with it.
    Splice EmptyMapping(YamlPath mappingPath)
    {
        var mapping = Locate(mappingPath);
        var map = mapping.Node;
        var end = YamlSplice.LineBreakAfter(Text, ContentEnd((int)map.Start.Index, YamlSplice.End(Text, map)));
        if (mapping.Key is { } key)
        {
            var colonEnd = YamlSplice.ColonEnd(Text, key);
            return new Splice(colonEnd, end - colonEnd, " { }" + CommentAfter(colonEnd));
        }

        var start = YamlSplice.ContentStart(Text, map);
        return new Splice(start, end - start, "{ }");
    }

    bool IsComment(int lineStart, Func<int, bool> column)
    {
        var line = YamlSplice.Line(Text, lineStart);
        var text = line.TrimStart(' ');
        return text.StartsWith('#') && column(line.Length - text.Length);
    }

    bool IsBlank(int lineStart) => YamlSplice.Line(Text, lineStart).Trim().Length == 0;
}
