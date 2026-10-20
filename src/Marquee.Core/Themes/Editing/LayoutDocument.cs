using Marquee.Core.Themes.Parsing;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// The text of one layout.yaml, with edits that change only what they mean to, as a person editing the file would:
/// comments, key order, blank lines, quoting and line endings everywhere else survive. YamlDotNet finds each node (its
/// nodes record where they start and end) and the change is spliced into the text. A document never changes: an edit
/// returns a new one, or throws LayoutEditException and leaves this one as it was.
///
/// Every edit is checked before it is returned. The new text must parse; every character outside the spliced span must
/// be the same as before; and what the file says (YamlTree) must be what it said before, changed only as the edit
/// means. A case the splicing gets wrong is refused, never written.
///
/// Refused: files with more than one YAML document or a key twice in one mapping (on Open), and any edit of a node
/// that has an anchor (`&base`) or is reached through an alias (`*base`), since the change would show up wherever
/// else it is used.
public sealed partial class LayoutDocument
{
    /// The UTF-8 byte order mark, as a character: what a text read without decoding it starts with.
    const char ByteOrderMark = '\uFEFF';

    readonly YamlNode? _root;
    readonly bool _bom;

    LayoutDocument(bool bom, string body, YamlNode? root)
    {
        _bom = bom;
        Body = body;
        Text = bom ? ByteOrderMark + body : body;
        _root = root;
        Eol = YamlSplice.LineEnding(body);

        // Measured here, not on first use: a document is shared between the UI thread and the render and autosave
        // threads, and an immutable one needs no locks.
        Step = FindStep(root, new(ReferenceEqualityComparer.Instance)) ?? 2;
        ListIndent = YamlStyle.ListIndent(body, root, Step);
    }

    /// The file's text, with its byte order mark when it had one.
    public string Text { get; }

    /// The text after any byte order mark: what YamlDotNet reads and what every splice and position counts in.
    /// YamlDotNet would read a byte order mark as part of the first key (`\uFEFFcanvas`), so it is kept aside.
    string Body { get; }

    /// The file's line ending, which every edit writes.
    string Eol { get; }

    /// Throws LayoutEditException, with the line at fault, for text that doesn't parse, holds more than one YAML
    /// document or has a key twice in one mapping. It doesn't check that the text is a valid theme: that is
    /// LayoutParser's job, and the editor opens a theme with an error so it can be fixed.
    ///
    /// A byte order mark at the start stays in Text, and every edit keeps it, so the file's bytes survive. File.ReadAllText
    /// and SafeFile drop one when they read, and SafeFile.WriteAllText writes none, so the editor only keeps one when it
    /// reads the file without decoding it: FromBytes and ToBytes do both ends.
    public static LayoutDocument Open(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bom = text.StartsWith(ByteOrderMark);
        var body = bom ? text[1..] : text;
        return new LayoutDocument(bom, body, Load(body));
    }

    /// True when the file has a node at `path`.
    public bool Exists(YamlPath path) => Resolve(path) is not null;

    /// True when the node at `path` can be edited: it exists, and neither it, nor anything above it, nor anything in it
    /// has an anchor or is an alias. It is per node: a widget with `style: *base` isn't editable as a whole (it can't be
    /// removed, moved or replaced), but its other properties are, so the editor asks for each (EditableKeys).
    public bool IsEditable(YamlPath path) =>
        Resolve(path) is { } at && !at.Chain.Any(IsAnchored) && !HoldsAnchor(at.Node, new(ReferenceEqualityComparer.Instance));

    static YamlNode? Load(string text)
    {
        if (LayoutScalar.LoneSurrogate(text) is var half and >= 0)
            throw new LayoutEditException(LayoutScalar.LoneSurrogateReason("The file"), text.AsSpan(0, half).Count('\n') + 1);

        var stream = new YamlStream();
        try
        {
            stream.Load(new DepthLimitedParser(new Parser(new StringReader(text)), Yaml.MaxDepth));
        }
        catch (YamlException ex) when (ex.Message.StartsWith(DuplicateKey, StringComparison.Ordinal))
        {
            throw new LayoutEditException(
                $"The key '{ex.Message[DuplicateKey.Length..].Trim()}' is in one mapping twice, so it isn't clear which " +
                "one counts. Remove one of them in the file.", (int)ex.Start.Line);
        }
        catch (YamlException ex)
        {
            throw new LayoutEditException($"YAML syntax error: {ex.Message}", (int)ex.Start.Line);
        }
        catch (InvalidOperationException ex)
        {
            // YamlDotNet throws this, without a position, for some malformed flow collections (Yaml.Load says more).
            throw new LayoutEditException($"YAML syntax error: {ex.Message} Check for an unclosed [ or {{.");
        }
        catch (ArgumentException ex)
        {
            // And this, also without a position, for some other malformed text: `- :`, or a tag that isn't a valid URI.
            throw new LayoutEditException($"YAML syntax error: {YamlFragment.Reason(ex)}");
        }
        catch (ThemeException ex)
        {
            throw new LayoutEditException(ex.Reason, ex.Line);
        }

        if (stream.Documents.Count > 1)
        {
            throw new LayoutEditException(
                "The file holds more than one YAML document (a line of --- starts another). A theme is one document: " +
                "remove the others in the file.", (int)stream.Documents[1].RootNode.Start.Line);
        }

        return stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode;
    }

    /// How YamlDotNet starts the message for a key that is in a mapping twice.
    const string DuplicateKey = "Duplicate key";

    /// A node found by its path: the nodes from the top down to it (Chain), and its key when it is a mapping's value.
    sealed record Located(YamlPath Path, IReadOnlyList<YamlNode> Chain, YamlScalarNode? Key)
    {
        public YamlNode Node => Chain[^1];

        /// The mapping or list holding the node; null for the top.
        public YamlNode? Parent => Chain.Count > 1 ? Chain[^2] : null;

        /// True inside a `{ }` or `[ ]`, where a value must be written on one line, in flow style.
        public bool InFlow => Chain.Take(Chain.Count - 1).Any(YamlSplice.IsFlow);

        public int Line => (int)Node.Start.Line;
    }

    Located? Resolve(YamlPath path)
    {
        if (_root is null) return null;

        var chain = new List<YamlNode> { _root };
        YamlScalarNode? key = null;
        foreach (var segment in path.Segments)
        {
            var node = chain[^1];
            if (segment.Key is { } name)
            {
                if (node is not YamlMappingNode map) return null;
                var entry = map.Children.FirstOrDefault(e => e.Key is YamlScalarNode { Value: var k } && k == name);
                if (entry.Key is null) return null;
                (key, node) = ((YamlScalarNode)entry.Key, entry.Value);
            }
            else
            {
                if (node is not YamlSequenceNode list || segment.Index >= list.Children.Count) return null;
                (key, node) = (null, list.Children[segment.Index]);
            }

            chain.Add(node);
        }

        return new Located(path, chain, key);
    }

    Located Locate(YamlPath path) =>
        Resolve(path) ?? throw new LayoutEditException($"{Name(path)} isn't in the file.");

    /// Refuses an edit of a node with an anchor, or reached through an alias, or (with `inside`) holding either.
    static void CheckEditable(Located at, bool inside)
    {
        if (at.Chain.Any(IsAnchored) || (inside && HoldsAnchor(at.Node, new(ReferenceEqualityComparer.Instance))))
        {
            throw new LayoutEditException(
                $"{Name(at.Path)} uses an anchor (&) or alias (*), so changing it would change the other places that use " +
                "it too. Edit it in the file.", at.Line);
        }
    }

    /// YamlDotNet hands back the anchored node itself where an alias names it, so an alias is anchored too.
    static bool IsAnchored(YamlNode node) => !node.Anchor.IsEmpty;

    static bool HoldsAnchor(YamlNode node, HashSet<YamlNode> seen) =>
        IsAnchored(node) || (seen.Add(node) && node switch
        {
            YamlSequenceNode list => list.Children.Any(item => HoldsAnchor(item, seen)),
            YamlMappingNode map => map.Children.Any(e => HoldsAnchor(e.Key, seen) || HoldsAnchor(e.Value, seen)),
            _ => false,
        });

    /// Makes an edit: `plan` returns the splices and how the edit changes what the file says. The result is checked
    /// (the class comment says how); any failure refuses the edit.
    LayoutDocument Edit(YamlPath path, Func<(IReadOnlyList<Splice> Splices, Func<YamlTree?, YamlTree?> Change)> plan)
    {
        try
        {
            var (splices, change) = plan();
            var text = YamlSplice.Apply(Body, splices);
            if (text == Body) return this;

            YamlNode? root;
            try
            {
                root = Load(text);
            }
            catch (LayoutEditException)
            {
                throw new SpliceException("The result doesn't parse.");
            }

            if (!YamlSplice.OnlyChanged(Body, text, splices)
                || !YamlTree.SameDocument(change(YamlTree.From(_root)), YamlTree.From(root)))
                throw new SpliceException("The result isn't the file with just that change.");

            return new LayoutDocument(_bom, text, root);
        }
        catch (SpliceException ex)
        {
            throw new LayoutEditException(
                $"The editor can't change {Name(path)} without changing other parts of the file too. {ex.Message} " +
                "Edit it in the file.", Resolve(path)?.Line ?? 0);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidCastException or InvalidOperationException)
        {
            // What the splicing throws when it misreads the file's shape: a position or index past the end, a node of
            // another kind than it expected, an empty sequence. The edit is refused like any other, never written, and
            // the fault stays as the InnerException so the editor can log it. Other exceptions (a null reference, say)
            // aren't a misread file but a plain bug, and are left to surface.
            throw new LayoutEditException(
                $"The editor can't change {Name(path)}: it misread this part of the file ({ex.GetType().Name}). " +
                "Edit it in the file.", Resolve(path)?.Line ?? 0, ex);
        }
    }

    static string Name(YamlPath path) => path.IsRoot ? "the top of the file" : path.ToString();

    /// How many columns the file indents a block mapping under its key: the first such mapping's, or 2.
    int Step { get; }

    static int? FindStep(YamlNode? node, HashSet<YamlNode> seen)
    {
        if (node is null || !seen.Add(node)) return null;
        switch (node)
        {
            case YamlMappingNode map:
                foreach (var (key, value) in map.Children)
                {
                    if (value is YamlMappingNode { Style: MappingStyle.Block, Children.Count: > 0 } inner
                        && inner.Children.First().Key.Start.Line > key.Start.Line)
                        return (int)(inner.Children.First().Key.Start.Column - key.Start.Column);
                    if (FindStep(value, seen) is { } step) return step;
                }

                return null;
            case YamlSequenceNode list:
                return list.Children.Select(item => FindStep(item, seen)).FirstOrDefault(step => step is not null);
            default:
                return null;
        }
    }

    /// The column a node's line starts at, counted from 0: its indentation.
    int Indentation(int index)
    {
        var start = YamlSplice.LineStart(Body, index);
        var i = start;
        while (i < Body.Length && Body[i] == ' ') i++;
        return i - start;
    }

    /// The column of a character, counted from 0.
    int Column(int index) => index - YamlSplice.LineStart(Body, index);

    /// Just past the last character of a node's text that isn't a space or line break: a block scalar's text, or a
    /// block collection's, ends after the line breaks that follow it.
    int ContentEnd(int start, int end)
    {
        while (end > start && Body[end - 1] is ' ' or '\t' or '\r' or '\n') end--;
        return end;
    }

    /// The comment, if any, on the rest of the line from `index`, with the spaces before it: `   # the look`.
    string CommentAfter(int index)
    {
        var rest = Body[index..YamlSplice.LineBreakAfter(Body, index)];
        return rest.TrimStart().StartsWith('#') ? rest.TrimEnd() : "";
    }

    /// Where the line holding `index` ends, before its line break.
    int LineEnd(int index) => YamlSplice.LineBreakAfter(Body, index);
}
