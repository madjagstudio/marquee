using System.Globalization;
using System.Text;

namespace Marquee.Core.Themes.Editing;

/// Names one node of a YAML file by the keys and list indexes that lead to it from the top: `widgets[3].style.color`,
/// `background[0].hidden`, `widgets[5].children[0].at[1]`. The empty path is the whole file. A key that isn't a plain
/// name (letters, digits, `_` and `-`) is written in brackets and double quotes, with `\` before a `"` or `\` in it:
/// `styles["my style"].size`.
public sealed record YamlPath
{
    readonly YamlPathSegment[] _segments;

    YamlPath(YamlPathSegment[] segments) => _segments = segments;

    public static YamlPath Root { get; } = new([]);

    public IReadOnlyList<YamlPathSegment> Segments => _segments;

    public bool IsRoot => _segments.Length == 0;

    /// The path one step up, or null for the root.
    public YamlPath? Parent => IsRoot ? null : new(_segments[..^1]);

    /// The last step, or null for the root.
    public YamlPathSegment? Last => IsRoot ? null : _segments[^1];

    public YamlPath Child(string key) => new([.. _segments, YamlPathSegment.ForKey(key)]);

    public YamlPath Item(int index) => new([.. _segments, YamlPathSegment.ForIndex(index)]);

    /// Throws FormatException for text that isn't a path.
    public static YamlPath Parse(string text) =>
        TryParse(text, out var path) ? path : throw new FormatException($"'{text}' is not a path like widgets[3].style.color");

    public static bool TryParse(string text, out YamlPath path)
    {
        ArgumentNullException.ThrowIfNull(text);
        path = Root;
        var segments = new List<YamlPathSegment>();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                var segment = i + 1 < text.Length && text[i + 1] == '"' ? QuotedKey(text, ref i) : Index(text, ref i);
                if (segment is null) return false;
                segments.Add(segment.Value);
            }
            else
            {
                // A plain key starts the path or follows a dot.
                if (segments.Count > 0)
                {
                    if (text[i] != '.') return false;
                    i++;
                }

                var start = i;
                while (i < text.Length && IsNameChar(text[i])) i++;
                if (i == start) return false;
                segments.Add(YamlPathSegment.ForKey(text[start..i]));
            }
        }

        path = new YamlPath(segments.ToArray());
        return true;
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (segment.Key is not { } key)
                text.Append('[').Append(segment.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
            else if (key.All(IsNameChar))
                text.Append(text.Length > 0 ? "." : "").Append(key);
            else
                text.Append("[\"").Append(key.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"]");
        }

        return text.ToString();
    }

    public bool Equals(YamlPath? other) => other is not null && _segments.AsSpan().SequenceEqual(other._segments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in _segments) hash.Add(segment);
        return hash.ToHashCode();
    }

    static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-';

    /// `[12]`, from the `[` at `i`; leaves `i` after the `]`.
    static YamlPathSegment? Index(string text, ref int i)
    {
        var close = text.IndexOf(']', i);
        if (close < 0) return null;
        var digits = text[(i + 1)..close];
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)
            || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return null;
        i = close + 1;
        return YamlPathSegment.ForIndex(index);
    }

    /// `["any key"]`, from the `[` at `i`; leaves `i` after the `]`.
    static YamlPathSegment? QuotedKey(string text, ref int i)
    {
        var key = new StringBuilder();
        for (var j = i + 2; j < text.Length; j++)
        {
            switch (text[j])
            {
                case '\\' when j + 1 < text.Length:
                    key.Append(text[++j]);
                    break;
                case '"' when j + 1 < text.Length && text[j + 1] == ']' && key.Length > 0:
                    i = j + 2;
                    return YamlPathSegment.ForKey(key.ToString());
                case '"':
                    return null;
                default:
                    key.Append(text[j]);
                    break;
            }
        }

        return null;
    }
}

/// One step of a YamlPath: a mapping's key, or a list's index when Key is null.
public readonly record struct YamlPathSegment
{
    YamlPathSegment(string? key, int index) => (Key, Index) = (key, index);

    public string? Key { get; }

    public int Index { get; }

    public bool IsKey => Key is not null;

    public static YamlPathSegment ForKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new(key, 0);
    }

    public static YamlPathSegment ForIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new(null, index);
    }

    public override string ToString() => Key ?? $"[{Index.ToString(CultureInfo.InvariantCulture)}]";
}
