using System.Text;
using System.Text.RegularExpressions;
using Marquee.Core.Files;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Config;

/// Changes one setting in config.yaml the way a person would: only that value's characters change, so the user's
/// comments, key order, blank lines, quoting and line endings survive. YamlDotNet finds the value (its nodes record
/// where they start and end); the edit is spliced into the original text, never re-serialized.
public static class ConfigEditor
{

    /// A top-level key or one level below it: theme, lconnect.handover.
    static readonly Regex KeyPattern = new("^[a-z_]+(\\.[a-z_]+)?$", RegexOptions.CultureInvariant);

    /// `key` is a top-level key (`theme`) or one level down (`lconnect.handover`); `value` is the plain text to store,
    /// quoted in the file when YAML needs it. A missing key is added: at the end of the file at the top level, at the
    /// end of its parent mapping one level down (inside the braces for a `{ ... }` mapping), with a block-style parent
    /// created when there is none. The result keeps the file's line endings (CRLF for a file with none yet).
    ///
    /// Throws ConfigException, and changes nothing, for a file that doesn't parse (Marquee never writes over a broken
    /// file), one whose shape has no place for the key (`lconnect: off` when setting lconnect.handover), or one it
    /// can't edit without touching something else (an alias). Only YAML is checked, not the values of other keys: a
    /// bad value elsewhere is reported where config.yaml is read, and doesn't stop the tray switching theme.
    public static string Set(string yaml, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!KeyPattern.IsMatch(key))
            throw new ArgumentException($"'{key}' is not a config.yaml key like theme or lconnect.handover", nameof(key));

        var path = key.Split('.');
        var root = Load(yaml);
        if (root is not null and not YamlMappingNode)
            throw NoPlace(root, $"config.yaml must be a mapping of keys to values, so Marquee can't set {key}");

        try
        {
            var edit = Plan(yaml, (YamlMappingNode?)root, path, key, value, LineEnding(yaml));
            var result = string.Concat(yaml.AsSpan(0, edit.At), edit.Text, yaml.AsSpan(edit.At + edit.Length));

            // The edit is only ever meant to change the one value. Reading the result back proves it, so a case the
            // splicing gets wrong refuses the change instead of writing a damaged file.
            return Matches((YamlMappingNode?)root, Yaml.Load(result, ConfigParser.FileName), path, value)
                ? result
                : throw new CantEdit();
        }
        catch (Exception ex) when (ex is CantEdit or ThemeException)
        {
            throw new ConfigException($"Marquee can't change {key} without changing other parts of config.yaml too. Set it by hand.");
        }
    }

    /// Reads the file, applies Set and writes the result. A missing file is created holding just that key. Nothing is
    /// written when the value is already set. It goes through SafeFile.Edit, which reads and then replaces the file
    /// atomically under one held folder, refusing a path with a junction or link on it before the read as well as the
    /// write: the tray that calls this runs elevated and config.yaml sits in a folder the user's unelevated programs
    /// control, so a read through a link could copy an administrator's file into the user's config.yaml.
    public static void SetInFile(string path, string key, string value) => SafeFile.Edit(path, yaml => Set(yaml, key, value));

    /// Replace `Length` characters at `At` with `Text`.
    readonly record struct Edit(int At, int Length, string Text);

    /// The edit can't be made without touching more than the one value; Set turns it into a ConfigException.
    sealed class CantEdit : Exception;

    static YamlNode? Load(string yaml)
    {
        try
        {
            return Yaml.Load(yaml, ConfigParser.FileName);
        }
        catch (ThemeException ex)
        {
            throw new ConfigException(
                $"{ex.Reason.TrimEnd('.')}. Fix this first: Marquee won't change a config.yaml it can't read.", ex.Line);
        }
    }

    static Edit Plan(string yaml, YamlMappingNode? root, string[] path, string key, string value, string eol)
    {
        var (parentKey, existing) = Find(root, path[0]);

        if (path.Length == 1)
        {
            if (existing is null) return Add(yaml, root, $"{key}: {YamlScalar.Render(value)}", eol, atEndOfFile: true);
            return Replace(yaml, Scalar(existing, key), value);
        }

        var (parent, child) = (path[0], path[1]);
        var flowRoot = root is { Style: MappingStyle.Flow };
        switch (existing)
        {
            case null when flowRoot:
                return Add(yaml, root, $"{parent}: {{ {child}: {YamlScalar.Render(value)} }}", eol, atEndOfFile: true);
            case null:
                return Add(yaml, root, $"{parent}:{eol}  {child}: {YamlScalar.Render(value)}", eol, atEndOfFile: true);
            case YamlScalarNode { Value: "", Style: ScalarStyle.Plain } empty when flowRoot:
                return Fill(yaml, empty, $"{{ {child}: {YamlScalar.Render(value)} }}");
            case YamlScalarNode { Value: "", Style: ScalarStyle.Plain } empty:
                // `lconnect:` with nothing after it: the key goes on a new line under it, past any comment.
                var line = $"{eol}{new string(' ', (int)parentKey!.Start.Column - 1 + 2)}{child}: {YamlScalar.Render(value)}";
                var at = LineBreakAfter(yaml, (int)empty.Start.Index);
                return Insert(at, at == yaml.Length ? line + eol : line);
            case YamlMappingNode map:
                var (_, current) = Find(map, child);
                return current is null
                    ? Add(yaml, map, $"{child}: {YamlScalar.Render(value)}", eol, atEndOfFile: false)
                    : Replace(yaml, Scalar(current, key), value);
            default:
                throw NoPlace(existing, $"{parent} must be a mapping of keys to values, so Marquee can't set {key}");
        }
    }

    /// The value at `name` in `map`, and its key.
    static (YamlScalarNode? Key, YamlNode? Value) Find(YamlMappingNode? map, string name)
    {
        if (map is null) return (null, null);
        foreach (var (key, value) in map.Children)
            if (key is YamlScalarNode { Value: var text } scalar && text == name)
                return (scalar, NotAlias(value, after: key));

        return (null, null);
    }

    static YamlScalarNode Scalar(YamlNode node, string key) =>
        node as YamlScalarNode ?? throw NoPlace(node, $"{key} must be a single value, not a list or mapping, so Marquee can't set it");

    /// Swaps the value's characters. A block scalar (`|` or `>`) owns the line breaks after its text; they stay.
    /// An empty value (`theme:`) gets the new one after a space.
    static Edit Replace(string yaml, YamlScalarNode node, string value)
    {
        var start = (int)node.Start.Index;
        var length = (int)node.End.Index - start;
        if (length == 0) return Fill(yaml, node, YamlScalar.Render(value));

        var old = yaml.Substring(start, length);
        var tail = old[old.TrimEnd().Length..];
        var lineBreak = tail.IndexOfAny(['\r', '\n']);
        var keep = lineBreak < 0 ? "" : tail[lineBreak..];
        var text = YamlScalar.Render(value, node.Style) + keep;
        return text == old ? new Edit(start, 0, "") : new Edit(start, length, text);
    }

    /// Writes into an empty value, which YamlDotNet places right after the colon in block style and at the next
    /// token (`}` or `,`) in flow style.
    static Edit Fill(string yaml, YamlScalarNode empty, string text)
    {
        var at = (int)empty.Start.Index;
        var before = at > 0 && yaml[at - 1] == ':' ? " " : "";
        var after = at < yaml.Length && yaml[at] is '}' or ']' or ',' ? " " : "";
        return Insert(at, before + text + after);
    }

    /// Adds `entry` (`key: value`, or a parent with its child) to a mapping: inside the braces of a flow mapping, else
    /// on its own line at the mapping's indentation, at the end of the file or right after the mapping's last entry.
    static Edit Add(string yaml, YamlMappingNode? map, string entry, string eol, bool atEndOfFile)
    {
        if (map is { Style: MappingStyle.Flow })
        {
            if (map.Children.Count > 0) return Insert(End(yaml, map.Children.Last().Value), ", " + entry);
            var open = (int)map.Start.Index + 1;
            return Insert(open, Closing(yaml, open) == open ? $" {entry} " : $" {entry}");
        }

        var indent = map is null ? "" : new string(' ', (int)map.Start.Column - 1);
        var lines = indent + entry.Replace(eol, eol + indent) + eol;
        var at = atEndOfFile || map is null ? yaml.Length : LineStartAfter(yaml, End(yaml, map.Children.Last().Value));
        var needsBreak = at == yaml.Length && yaml.Length > 0 && yaml[^1] != '\n';
        return Insert(at, needsBreak ? eol + lines : lines);
    }

    static Edit Insert(int at, string text) => new(at, 0, text);

    /// Where a node's text ends. YamlDotNet records that for scalars but not collections (their End is their Start),
    /// so a block collection ends where its last item does and a flow collection after its closing bracket.
    static int End(string yaml, YamlNode node)
    {
        if (node is YamlScalarNode) return (int)node.End.Index;

        var last = node switch
        {
            YamlMappingNode { Children.Count: > 0 } map => NotAlias(map.Children.Last().Value, after: map.Children.Last().Key),
            YamlSequenceNode { Children.Count: > 0 } list => NotAlias(list.Children.Last(), after: list),
            _ => null,
        };
        var flow = node is YamlMappingNode { Style: MappingStyle.Flow } or YamlSequenceNode { Style: SequenceStyle.Flow };
        if (!flow) return End(yaml, last!);
        return Closing(yaml, last is null ? (int)node.Start.Index + 1 : End(yaml, last)) + 1;
    }

    /// A node always starts after its key, or after the start of the list holding it, unless it is an alias
    /// (`theme: *t`): YamlDotNet hands back the anchored node, whose text is elsewhere in the file (or, for a list
    /// holding an alias of itself, is the list), so it can't be edited or measured here.
    static YamlNode NotAlias(YamlNode node, YamlNode after) =>
        node.Start.Index > after.Start.Index ? node : throw new CantEdit();

    /// The closing bracket of a flow collection, searching from just after its last item: only spaces, line breaks,
    /// a trailing comma and comments can come between.
    static int Closing(string yaml, int from)
    {
        for (var i = from; i < yaml.Length; i++)
        {
            switch (yaml[i])
            {
                case '}' or ']':
                    return i;
                case ' ' or '\t' or '\r' or '\n' or ',':
                    continue;
                case '#':
                    while (i < yaml.Length && yaml[i] != '\n') i++;
                    continue;
                default:
                    throw new CantEdit();
            }
        }

        throw new CantEdit();
    }

    /// Where the line holding `index` ends, before its line break (or the end of the file).
    static int LineBreakAfter(string yaml, int index)
    {
        var lf = yaml.IndexOf('\n', index);
        if (lf < 0) return yaml.Length;
        return lf > 0 && yaml[lf - 1] == '\r' ? lf - 1 : lf;
    }

    /// The start of the line after the one holding `index`. A block scalar's text already ends with its line break.
    static int LineStartAfter(string yaml, int index)
    {
        if (index > 0 && yaml[index - 1] == '\n') return index;
        var lf = yaml.IndexOf('\n', index);
        return lf < 0 ? yaml.Length : lf + 1;
    }

    /// The file's own line ending, from its first line break. A file with none gets Windows' CRLF, as Notepad writes.
    static string LineEnding(string yaml)
    {
        var lf = yaml.IndexOf('\n');
        return lf < 0 || (lf > 0 && yaml[lf - 1] == '\r') ? "\r\n" : "\n";
    }

    /// True when `after` is `before` with `value` at `path`: every other entry the same and in the same order, the
    /// edited one in its old place or, when it was missing, last. A parent that was empty counts as an empty mapping.
    static bool Matches(YamlMappingNode? before, YamlNode? after, string[] path, string value)
    {
        if (path.Length == 0) return after is YamlScalarNode { Value: var text } && text == value;
        if (after is not YamlMappingNode map) return false;

        var old = before?.Children.ToList() ?? [];
        var now = map.Children.ToList();
        var at = old.FindIndex(entry => entry.Key is YamlScalarNode { Value: var name } && name == path[0]);
        var target = at < 0 ? old.Count : at;
        if (now.Count != (at < 0 ? old.Count + 1 : old.Count)) return false;

        for (var i = 0; i < now.Count; i++)
        {
            if (i != target)
            {
                if (!Same(old[i].Key, now[i].Key) || !Same(old[i].Value, now[i].Value)) return false;
            }
            else if (now[i].Key is not YamlScalarNode { Value: var name } || name != path[0]
                     || !Matches(at < 0 ? null : old[at].Value as YamlMappingNode, now[i].Value, path[1..], value))
            {
                return false;
            }
        }

        return true;
    }

    /// The same content. The depth limit stops at an alias of a list inside itself, which YAML allows; such a file
    /// is refused rather than compared forever.
    static bool Same(YamlNode a, YamlNode b, int depth = 0) => depth < 64 && (a, b) switch
    {
        (YamlScalarNode x, YamlScalarNode y) => x.Value == y.Value && x.Tag.Equals(y.Tag),
        (YamlSequenceNode x, YamlSequenceNode y) => x.Children.Count == y.Children.Count
            && x.Children.Zip(y.Children).All(p => Same(p.First, p.Second, depth + 1)),
        (YamlMappingNode x, YamlMappingNode y) => x.Children.Count == y.Children.Count
            && x.Children.Zip(y.Children).All(p => Same(p.First.Key, p.Second.Key, depth + 1) && Same(p.First.Value, p.Second.Value, depth + 1)),
        _ => false,
    };

    static ConfigException NoPlace(YamlNode node, string reason) =>
        new($"{reason}. Fix config.yaml first.", Yaml.Line(node));
}
