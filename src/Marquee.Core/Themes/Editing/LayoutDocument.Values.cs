using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// Setting a single value, and replacing a whole one.
public sealed partial class LayoutDocument
{
    /// Sets the single value at `path` to `value`, in the value's own style where that can hold it (LayoutScalar):
    /// plain stays plain, quotes stay the same quotes, and a `|` or `>` block stays a block. Any tag in front of it
    /// stays. Setting the value it already has returns this document.
    public LayoutDocument SetScalar(YamlPath path, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (LayoutScalar.LoneSurrogate(value) >= 0) throw new LayoutEditException(LayoutScalar.LoneSurrogateReason("The new value"));
        var at = Locate(path);
        CheckEditable(at, inside: true);
        if (at.Node is not YamlScalarNode scalar)
            throw new LayoutEditException($"{Name(path)} is a list or mapping, not a single value.", at.Line);
        if (scalar.Value == value) return this;

        return Edit(path, () => (
            [YamlSplice.ReplaceScalar(Body, scalar, Render(at, scalar, value))],
            tree => YamlTree.Change(tree, path, old => new YamlTreeScalar(value, old!.Tag, ""))));
    }

    /// Replaces the whole value at `path` (a single value, a mapping or a list) with `yaml`, the new value's YAML text
    /// written from column 0 in the style of its place: on one line inside `{ }` or `[ ]`, and in either style
    /// elsewhere. LayoutDocument indents it to fit. Comments inside the old value go with it; a comment on the key's
    /// line stays.
    public LayoutDocument ReplaceValue(YamlPath path, string yaml)
    {
        var at = Locate(path);
        if (path.IsRoot) throw new LayoutEditException("The editor replaces values under keys and in lists, not the whole file.");
        CheckEditable(at, inside: true);
        var fragment = Fragment(yaml);
        if (fragment.IsMultiLine && at.InFlow)
            throw new LayoutEditException($"{Name(path)} is inside {{ }} or [ ], so its new value must be written on one line.", at.Line);

        return Edit(path, () => ([Place(at, fragment)], tree => YamlTree.Change(tree, path, _ => fragment.Tree)));
    }

    static YamlFragment Fragment(string yaml)
    {
        try
        {
            return YamlFragment.Parse(yaml);
        }
        catch (SpliceException ex)
        {
            throw new LayoutEditException(ex.Message);
        }
    }

    string Render(Located at, YamlScalarNode scalar, string value)
    {
        if (scalar.Style is not (ScalarStyle.Literal or ScalarStyle.Folded))
            return LayoutScalar.Inline(value, scalar.Style, at.InFlow);

        // The block's header line: its indicator (`|-`, `>2`), then perhaps a comment, which stays.
        var start = YamlSplice.ContentStart(Body, scalar);
        var header = Body[start..YamlSplice.LineBreakAfter(Body, start)];
        var rest = header.TrimStart('|', '>', '+', '-', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return LayoutScalar.Block(value, scalar.Style, rest, new string(' ', BlockIndent(scalar, start)), Eol)
            ?? LayoutScalar.Inline(value, ScalarStyle.DoubleQuoted, at.InFlow) + rest.TrimEnd();
    }

    /// The indentation of a block scalar's text: its first line's, or one step in from its header's line.
    int BlockIndent(YamlScalarNode scalar, int start)
    {
        var end = (int)scalar.End.Index;
        for (var line = YamlSplice.LineStartAfter(Body, start); line < end; line = YamlSplice.NextLine(Body, line))
            if (YamlSplice.Line(Body, line).Trim().Length > 0) return Indentation(line);
        return Indentation(start) + Step;
    }

    /// The splice that puts `fragment` where the node at `at` is. A one-line value goes where the old one was. A
    /// block mapping or list goes on the lines under its key, one step in (or, replacing a block, at the old block's
    /// indentation), and as a list item starts on the item's line (`- size: 30`). A value of several lines that isn't
    /// a block (a `|` text) starts after its key, its later lines indented as written, from the key's column.
    Splice Place(Located at, YamlFragment fragment)
    {
        var node = at.Node;
        var key = at.Key;
        if (YamlSplice.IsEmpty(node))
        {
            if (!fragment.IsMultiLine) return YamlSplice.Fill(Body, (YamlScalarNode)node, fragment.Lines[0]);
            if (key is null) throw new SpliceException("An empty list item can only take a one-line value.");
            var colon = YamlSplice.ColonEnd(Body, key);
            var column = Column((int)key.Start.Index);
            return fragment.IsBlockCollection
                ? Splice.Insert(YamlSplice.LineBreakAfter(Body, colon), Eol + fragment.Indented(column + Step, true, Eol))
                : Splice.Insert(colon, " " + fragment.Indented(column, false, Eol));
        }

        var start = (int)node.Start.Index;
        var end = YamlSplice.End(Body, node);
        var contentEnd = ContentEnd(start, end);
        var oldIsBlock = node is YamlMappingNode or YamlSequenceNode && !YamlSplice.IsFlow(node);

        if (key is null)
        {
            // A list item: everything starts on the item's line, the later lines at the item's column.
            var first = oldIsBlock ? YamlSplice.ContentStart(Body, node) : start;
            return new Splice(first, contentEnd - first, fragment.Indented(Column(first), false, Eol));
        }

        var keyColumn = Column((int)key.Start.Index);
        var colonEnd = YamlSplice.ColonEnd(Body, key);
        if (oldIsBlock)
        {
            if (fragment.IsBlockCollection)
            {
                var first = YamlSplice.ContentStart(Body, node);
                return new Splice(first, contentEnd - first, fragment.Indented(Column(first), false, Eol));
            }

            // The old value's comments go with it, the one on its last line too. A comment on the key's line describes
            // the key, so it stays there when the value moves up beside it.
            var text = " " + fragment.Indented(keyColumn, false, Eol);
            return new Splice(colonEnd, LineEnd(contentEnd) - colonEnd, Commented(fragment, text, CommentAfter(colonEnd)));
        }

        if (!fragment.IsBlockCollection)
        {
            // A comment after the old value stays with the new one.
            var after = CommentAfter(contentEnd);
            var replaced = after.Length == 0 ? contentEnd : LineEnd(contentEnd);
            return new Splice(start, replaced - start, Commented(fragment, fragment.Indented(keyColumn, false, Eol), after));
        }

        // A one-line value becomes a block under its key; a comment after the old value moves up to the key's line.
        var lineEnd = YamlSplice.LineBreakAfter(Body, contentEnd);
        return new Splice(colonEnd, lineEnd - colonEnd,
            CommentAfter(contentEnd) + Eol + fragment.Indented(keyColumn + Step, true, Eol));
    }

    /// `text`, a fragment as written into the file, with `comment` (perhaps none) after it: after its last line, or
    /// after the header line of a `|` or `>` text, which runs to its last line and would take the comment in.
    string Commented(YamlFragment fragment, string text, string comment)
    {
        if (comment.Length == 0 || fragment is not { IsBlockScalar: true, IsMultiLine: true }) return text + comment;
        var header = text.IndexOf(Eol, StringComparison.Ordinal);
        return text[..header] + comment + text[header..];
    }
}
