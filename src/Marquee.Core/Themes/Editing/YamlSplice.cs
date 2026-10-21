using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// Replace `Length` characters at `At` with `Text`.
readonly record struct Splice(int At, int Length, string Text)
{
    public int End => At + Length;

    public static Splice Insert(int at, string text) => new(at, 0, text);
}

/// An edit that can't be spliced without touching more than it means to. ConfigEditor and LayoutDocument each turn
/// it into their own refusal; the reason is for LayoutDocument's.
sealed class SpliceException(string reason) : Exception(reason);

/// The in-place YAML editing that ConfigEditor and LayoutDocument share. YamlDotNet finds a value (its nodes record
/// where they start and end) and the change is spliced into the original text, never re-serialized, so comments, key
/// order, blank lines, quoting and line endings survive. This holds what both need: where a node's text starts and
/// ends, the closing bracket of a `{ }` or `[ ]`, lines and indentation, and the splices themselves.
static class YamlSplice
{
    /// The text with every splice made. Splices are placed by their position in the original text and must not
    /// overlap; two inserts at one position go in the order given.
    public static string Apply(string text, IReadOnlyList<Splice> splices)
    {
        var result = new StringBuilder(text.Length + splices.Sum(s => s.Text.Length));
        var at = 0;
        foreach (var splice in Ordered(splices))
        {
            if (splice.At < at || splice.End > text.Length) throw new SpliceException("The edit's changes overlap.");
            result.Append(text, at, splice.At - at).Append(splice.Text);
            at = splice.End;
        }

        return result.Append(text, at, text.Length - at).ToString();
    }

    /// True when `after` is `before` with only the splices' characters changed: every character outside them is
    /// still there, unchanged and in order. Apply builds texts that pass; this proves it for each edit.
    public static bool OnlyChanged(string before, string after, IReadOnlyList<Splice> splices)
    {
        var (at, shift) = (0, 0);
        foreach (var splice in Ordered(splices))
        {
            if (!Same(before, at, after, at + shift, splice.At - at)
                || !Same(splice.Text, 0, after, splice.At + shift, splice.Text.Length))
                return false;
            shift += splice.Text.Length - splice.Length;
            at = splice.End;
        }

        return after.Length == before.Length + shift && Same(before, at, after, at + shift, before.Length - at);
    }

    static IEnumerable<Splice> Ordered(IReadOnlyList<Splice> splices) => splices.OrderBy(s => s.At);

    static bool Same(string a, int aAt, string b, int bAt, int length) =>
        length >= 0 && aAt + length <= a.Length && bAt >= 0 && bAt + length <= b.Length
        && a.AsSpan(aAt, length).SequenceEqual(b.AsSpan(bAt, length));

    /// Where a node's own text starts, after any tag (`!!str`) or anchor (`&base`) in front of it, which YamlDotNet
    /// counts as part of the node.
    public static int ContentStart(string text, YamlNode node)
    {
        var i = (int)node.Start.Index;
        while (i < text.Length && text[i] is '!' or '&')
        {
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            i = SkipSpace(text, i);
        }

        return i;
    }

    /// Past spaces, line breaks and comments.
    static int SkipSpace(string text, int i)
    {
        while (i < text.Length)
        {
            if (text[i] is ' ' or '\t' or '\r' or '\n') i++;
            else if (text[i] == '#') i = LineBreakAfter(text, i);
            else break;
        }

        return i;
    }

    /// Where a node's text ends. YamlDotNet records that for scalars but not collections (their End is their Start),
    /// so a block collection ends where its last item does and a flow collection after its closing bracket. A block
    /// scalar (`|` or `>`) ends after the line breaks that follow its text, and an empty value with a tag or anchor
    /// (`a: !t`) after them.
    public static int End(string text, YamlNode node)
    {
        if (node is YamlScalarNode scalar) return IsTaggedEmpty(text, scalar) ? PropertiesEnd(text, scalar) : (int)node.End.Index;

        var last = node switch
        {
            YamlMappingNode { Children.Count: > 0 } map => NotAlias(map.Children.Last().Value, after: map.Children.Last().Key),
            YamlSequenceNode { Children.Count: > 0 } list => NotAlias(list.Children.Last(), after: list),
            _ => null,
        };
        if (!IsFlow(node)) return End(text, last!);
        return Closing(text, last is null ? ContentStart(text, node) + 1 : End(text, last)) + 1;
    }

    /// Where a mapping entry ends: after its value, or after the colon when the value is empty (`name:`), which
    /// YamlDotNet places at the next token in flow style.
    public static int EntryEnd(string text, YamlNode key, YamlNode value)
    {
        value = NotAlias(value, after: key);
        return IsEmpty(value) ? ColonEnd(text, key) : End(text, value);
    }

    /// Just after the colon that follows a key.
    public static int ColonEnd(string text, YamlNode key)
    {
        var i = (int)key.End.Index;
        while (i < text.Length && text[i] is ' ' or '\t') i++;
        return i < text.Length && text[i] == ':' ? i + 1 : throw new SpliceException("A key is written in a form the editor can't edit.");
    }

    /// An empty value: `name:` with nothing after it.
    public static bool IsEmpty(YamlNode node) =>
        node is YamlScalarNode { Value: "", Style: ScalarStyle.Plain } && node.End.Index == node.Start.Index;

    /// An empty value with a tag or anchor in front of it: `a: !t`. YamlDotNet starts it at the tag but ends it at
    /// the next token, past any blank and comment lines between, which belong to what follows.
    static bool IsTaggedEmpty(string text, YamlScalarNode node) =>
        node is { Value: "", Style: ScalarStyle.Plain } && node.End.Index > node.Start.Index
        && node.Start.Index < text.Length && text[(int)node.Start.Index] is '!' or '&';

    /// Just after the tags and anchors a node's text starts with on its line.
    static int PropertiesEnd(string text, YamlNode node)
    {
        var i = (int)node.Start.Index;
        var end = i;
        while (i < text.Length && text[i] is '!' or '&')
        {
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            end = i;
            while (i < text.Length && text[i] is ' ' or '\t') i++;
        }

        return Math.Min(end, (int)node.End.Index);
    }

    public static bool IsFlow(YamlNode node) =>
        node is YamlMappingNode { Style: MappingStyle.Flow } or YamlSequenceNode { Style: SequenceStyle.Flow };

    /// A node always starts after its key, or after the start of the list holding it, unless it is an alias
    /// (`theme: *t`): YamlDotNet hands back the anchored node, whose text is elsewhere in the file (or, for a list
    /// holding an alias of itself, is the list), so it can't be edited or measured here.
    public static YamlNode NotAlias(YamlNode node, YamlNode after) =>
        node.Start.Index > after.Start.Index ? node : throw new SpliceException("It uses an alias (*).");

    /// The closing bracket of a flow collection, searching from just after its last item: only spaces, line breaks,
    /// a trailing comma and comments can come between.
    public static int Closing(string text, int from)
    {
        for (var i = from; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '}' or ']':
                    return i;
                case ' ' or '\t' or '\r' or '\n' or ',':
                    continue;
                case '#':
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                default:
                    throw new SpliceException("A { } or [ ] holds something the editor can't read.");
            }
        }

        throw new SpliceException("A { } or [ ] isn't closed.");
    }

    /// The column, counted from 0, at which a block mapping's keys start.
    public static int KeyColumn(YamlMappingNode map) =>
        (int)(map.Children.Count > 0 ? map.Children.First().Key.Start.Column : map.Start.Column) - 1;

    /// Adds `entry` (`key: value`, whose later lines are already indented relative to the key) to a mapping: inside
    /// the braces of a flow mapping, else on its own line at the mapping's indentation, at the end of the file or right
    /// after the mapping's last entry. A null mapping is an empty file.
    public static Splice AddToMapping(string text, YamlMappingNode? map, string entry, string eol, bool atEndOfFile)
    {
        if (map is { Style: MappingStyle.Flow })
        {
            if (map.Children.Count > 0)
            {
                // After an empty value the comma keeps a space before it, as in `{ name: , b: 1 }`.
                var (key, value) = map.Children.Last();
                return Splice.Insert(EntryEnd(text, key, value), (IsEmpty(value) ? " " : "") + Separator(text, map) + entry);
            }

            var open = ContentStart(text, map) + 1;
            return Splice.Insert(open, Closing(text, open) == open ? $" {entry} " : $" {entry}");
        }

        var indent = map is null ? "" : new string(' ', KeyColumn(map));
        var lines = indent + entry.Replace(eol, eol + indent) + eol;
        var at = atEndOfFile || map is null
            ? text.Length
            : LineStartAfter(text, EntryEnd(text, map.Children.Last().Key, map.Children.Last().Value));
        var needsBreak = at == text.Length && text.Length > 0 && text[^1] != '\n';
        return Splice.Insert(at, needsBreak ? eol + lines : lines);
    }

    /// What goes between a flow mapping's last entry and a new one: what its entries already have between them (`,`
    /// in `{a: 1,b: 2}`, or a comma and a new line in a `{ }` written over several lines), or `, `.
    static string Separator(string text, YamlMappingNode map)
    {
        if (map.Children.Count < 2) return ", ";
        var (key, value) = map.Children.ElementAt(map.Children.Count - 2);
        var between = text[EntryEnd(text, key, value)..(int)map.Children.Last().Key.Start.Index];
        return between.Count(c => c == ',') == 1 && between.All(c => c is ',' or ' ' or '\r' or '\n') ? between : ", ";
    }

    /// Swaps a scalar's characters for `rendered`, keeping any tag in front of it. A block scalar (`|` or `>`) owns
    /// the line breaks after its text; they stay. An empty value (`name:`) is filled, and one with a tag (`name: !t`)
    /// gets the new value after its tag.
    public static Splice ReplaceScalar(string text, YamlScalarNode node, string rendered)
    {
        if (node.End.Index == node.Start.Index) return Fill(text, node, rendered);
        if (IsTaggedEmpty(text, node)) return Splice.Insert(PropertiesEnd(text, node), " " + rendered);

        var end = (int)node.End.Index;
        var start = Math.Min(ContentStart(text, node), end);
        var old = text[start..end];
        var tail = old[old.TrimEnd().Length..];
        var lineBreak = tail.IndexOfAny(['\r', '\n']);
        var replacement = rendered + (lineBreak < 0 ? "" : tail[lineBreak..]);
        return replacement == old ? new Splice(start, 0, "") : new Splice(start, end - start, replacement);
    }

    /// Writes into an empty value, which YamlDotNet places right after the colon in block style and at the next
    /// token (`}`, `]` or `,`) in flow style: `name:` becomes `name: x`, `{ name: }` becomes `{ name: x }` and
    /// `{ name: , b: 1 }` becomes `{ name: x, b: 1 }`.
    public static Splice Fill(string text, YamlScalarNode empty, string rendered)
    {
        var at = (int)empty.Start.Index;
        var closes = at < text.Length && text[at] is '}' or ']';
        if (at > 0 && text[at - 1] == ':') return Splice.Insert(at, " " + rendered + (closes ? " " : ""));
        if (closes) return Splice.Insert(at, rendered + " ");

        var colon = at;
        while (colon > 0 && text[colon - 1] is ' ' or '\t') colon--;
        return new Splice(colon, at - colon, " " + rendered);
    }

    /// The start of the line holding `index`.
    public static int LineStart(string text, int index) => index == 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;

    /// Where the line holding `index` ends, before its line break (or the end of the file).
    public static int LineBreakAfter(string text, int index)
    {
        var lf = text.IndexOf('\n', index);
        if (lf < 0) return text.Length;
        return lf > 0 && text[lf - 1] == '\r' ? lf - 1 : lf;
    }

    /// The start of the line after the one holding `index`. A block scalar's text already ends with its line break.
    public static int LineStartAfter(string text, int index)
    {
        if (index > 0 && text[index - 1] == '\n') return index;
        var lf = text.IndexOf('\n', index);
        return lf < 0 ? text.Length : lf + 1;
    }

    /// The start of the line after the one starting at `lineStart` (or the end of the file).
    public static int NextLine(string text, int lineStart)
    {
        var lf = text.IndexOf('\n', lineStart);
        return lf < 0 ? text.Length : lf + 1;
    }

    /// The line from `lineStart`, without its line break.
    public static string Line(string text, int lineStart) => text[lineStart..LineBreakAfter(text, lineStart)];

    /// The file's own line ending, from its first line break. A file with none gets Windows' CRLF, as Notepad writes.
    public static string LineEnding(string text)
    {
        var lf = text.IndexOf('\n');
        return lf < 0 || (lf > 0 && text[lf - 1] == '\r') ? "\r\n" : "\n";
    }
}
