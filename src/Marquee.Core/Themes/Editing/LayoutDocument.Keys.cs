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
                [YamlSplice.AddToMapping(Body, null, Entry(LayoutScalar.Key(key, false), fragment), Eol, atEndOfFile: true)],
                _ => Added(null)));
        }

        var at = Locate(mapping);
        CheckEditable(at, inside: false);
        var inFlow = at.InFlow || YamlSplice.IsFlow(at.Node);
        if (inFlow && fragment.IsMultiLine)
            throw new LayoutEditException($"{Name(mapping)} is a {{ }} mapping, so the value of {key} must be written on one line.", at.Line);

        var keyText = LayoutScalar.Key(key, inFlow);

        // Planned inside Edit, so a mapping the splicing can't measure (its last value an alias, its last key a
        // complex `? key`) is refused like any other edit.
        return Edit(mapping, () =>
        {
            Splice splice;
            switch (at.Node)
            {
                case YamlMappingNode map when map.Children.Any(e => e.Key is YamlScalarNode { Value: var k } && k == key):
                    throw new LayoutEditException($"{Name(mapping)} already has {key}.", at.Line);
                case YamlMappingNode map:
                    splice = YamlSplice.AddToMapping(Body, map, Entry(keyText, fragment), Eol, atEndOfFile: false);
                    break;
                case YamlScalarNode empty when YamlSplice.IsEmpty(empty) && inFlow:
                    splice = YamlSplice.Fill(Body, empty, $"{{ {keyText}: {fragment.Lines[0]} }}");
                    break;
                case YamlScalarNode empty when YamlSplice.IsEmpty(empty) && at.Key is { } parentKey:
                    // `style:` with nothing after it: the key goes on a new line under it, past any comment.
                    var indent = Pad(Column((int)parentKey.Start.Index) + Step);
                    var lines = indent + Entry(keyText, fragment).Replace(Eol, Eol + indent);
                    var lineEnd = YamlSplice.LineBreakAfter(Body, (int)empty.Start.Index);
                    splice = Splice.Insert(lineEnd, Eol + lines + (lineEnd == Body.Length ? Eol : ""));
                    break;
                default:
                    throw new LayoutEditException($"{Name(mapping)} isn't a mapping of keys to values, so it can't take {key}.", at.Line);
            }

            return ([splice], tree => YamlTree.Change(tree, mapping, Added));
        });
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
    /// A list item's first key, on the dash's line, goes with its value's lines, and the next key moves up beside the
    /// dash; the comment lines right above that key, which describe it, go above the dash. Removing the only key of a
    /// block mapping leaves `{ }`, so the mapping stays a mapping (`style: { }`).
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
            YamlSplice.IsFlow(map) ? [RemoveFromFlow(map, entries, index)] : RemoveFromBlock(at, entries, index),
            tree => YamlTree.Change(tree, path.Parent!, m => ((YamlTreeMap)m!).Without(last.Key!))));
    }

    Splice RemoveFromFlow(YamlMappingNode map, List<KeyValuePair<YamlNode, YamlNode>> entries, int index)
    {
        var (key, value) = entries[index];
        if (entries.Count == 1)
        {
            var open = YamlSplice.ContentStart(Body, map) + 1;
            var close = YamlSplice.Closing(Body, YamlSplice.EntryEnd(Body, key, value));
            return new Splice(open, close - open, " ");
        }

        // Up to the next key, taking the comma; the last entry takes the comma before it instead.
        if (index < entries.Count - 1)
        {
            var next = (int)entries[index + 1].Key.Start.Index;
            return new Splice((int)key.Start.Index, next - (int)key.Start.Index, "");
        }

        var previous = YamlSplice.EntryEnd(Body, entries[index - 1].Key, entries[index - 1].Value);
        return new Splice(previous, YamlSplice.EntryEnd(Body, key, value) - previous, "");
    }

    List<Splice> RemoveFromBlock(Located at, List<KeyValuePair<YamlNode, YamlNode>> entries, int index)
    {
        var (key, value) = entries[index];
        if (entries.Count == 1 && at.Chain.Count > 2) return [EmptyMapping(at.Path.Parent!)];

        var keyStart = (int)key.Start.Index;
        var start = YamlSplice.LineStart(Body, keyStart);
        if (Body.AsSpan(start, keyStart - start).Trim(" \t") is "-")
        {
            // The first key of a list item, on the dash's line (`- type: text`): the next key moves up beside the dash.
            // The comment lines right above the next key, at its column, describe it, so they go above the dash, at
            // its column: where comments describing the item's first key sit.
            var next = (int)entries[index + 1].Key.Start.Index;
            var nextLine = YamlSplice.LineStart(Body, next);
            var comments = nextLine;
            while (comments > start && IsComment(YamlSplice.LineStart(Body, comments - 1), c => c == next - nextLine))
                comments = YamlSplice.LineStart(Body, comments - 1);

            var removal = new Splice(keyStart, next - keyStart, "");
            if (comments == nextLine) return [removal];
            var pad = Pad(Indentation(start));
            var moved = Body[comments..nextLine].Split('\n').SkipLast(1).Select(line => pad + line.TrimStart(' ').TrimEnd('\r') + Eol);
            return [Splice.Insert(start, string.Concat(moved)), removal];
        }

        // The key's column, or, for a complex key (`? key`), its question mark's.
        var column = Indentation(keyStart);
        var end = YamlSplice.LineStartAfter(Body, ContentEnd(keyStart, YamlSplice.EntryEnd(Body, key, value)));

        var above = start;
        while (above > 0 && IsComment(YamlSplice.LineStart(Body, above - 1), c => c == column))
            above = YamlSplice.LineStart(Body, above - 1);
        if (above > 0) start = above;

        while (end < Body.Length && IsComment(end, c => c > column)) end = YamlSplice.NextLine(Body, end);
        return [RemoveLines(start, end)];
    }

    /// Removes the lines from `start` to `end` (line starts), with a blank line when they had one above and below
    /// them, so their neighbours keep one blank line between them.
    Splice RemoveLines(int start, int end)
    {
        var blankAbove = start == 0 || IsBlank(YamlSplice.LineStart(Body, start - 1));
        if (blankAbove && end < Body.Length && IsBlank(end)) end = YamlSplice.NextLine(Body, end);
        else if (start > 0 && end == Body.Length && IsBlank(YamlSplice.LineStart(Body, start - 1)))
            start = YamlSplice.LineStart(Body, start - 1);

        return new Splice(start, end - start, "");
    }

    /// Removing the only key of a block mapping: the mapping becomes `{ }`, after its key (keeping a comment on the
    /// key's line) or on its list item's line. The removed key's own comment on its line goes with it.
    Splice EmptyMapping(YamlPath mappingPath)
    {
        var mapping = Locate(mappingPath);
        var map = mapping.Node;
        var end = YamlSplice.LineBreakAfter(Body, ContentEnd((int)map.Start.Index, YamlSplice.End(Body, map)));
        if (mapping.Key is { } key)
        {
            var colonEnd = YamlSplice.ColonEnd(Body, key);
            return new Splice(colonEnd, end - colonEnd, " { }" + CommentAfter(colonEnd));
        }

        var start = YamlSplice.ContentStart(Body, map);
        return new Splice(start, end - start, "{ }");
    }

    bool IsComment(int lineStart, Func<int, bool> column)
    {
        var line = YamlSplice.Line(Body, lineStart);
        var text = line.TrimStart(' ');
        return text.StartsWith('#') && column(line.Length - text.Length);
    }

    bool IsBlank(int lineStart) => YamlSplice.Line(Body, lineStart).Trim().Length == 0;
}
