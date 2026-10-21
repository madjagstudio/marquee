using Marquee.Core.Config;
using YamlDotNet.Core;

namespace Marquee.Core.Themes.Editing;

/// A value written into layout.yaml in place of an existing one, in that value's style where the style can hold it.
/// Unlike config.yaml's words, a layout's plain values are numbers, words like `bold`, `true` and short texts, so any
/// text YAML reads back unchanged stays plain: `120`, `-12.5`, `true`, `GPU LOAD`. LayoutParser reads a value's text,
/// not a type YAML infers from it, so quoting `120` wouldn't change what it means, only how it looks.
static class LayoutScalar
{
    /// Characters that can't start a plain value. `-`, `?` and `:` can, before a character that isn't a space.
    const string Indicators = "-?:,[]{}#&*!|>'\"%@`";

    /// Characters that end a plain value inside `{ }` or `[ ]`.
    const string FlowIndicators = ",[]{}";

    /// The value as a plain, single-quoted or double-quoted scalar. A double-quoted value stays so; a value with a line
    /// break or control character needs double quotes and escapes; a single-quoted one stays so; a plain one stays
    /// plain when YAML reads it back as the same text there, and is single-quoted otherwise.
    public static string Inline(string value, ScalarStyle style, bool inFlow)
    {
        if (style == ScalarStyle.DoubleQuoted || value.Any(YamlScalar.NeedsEscape)) return YamlScalar.DoubleQuoted(value);
        if (style == ScalarStyle.SingleQuoted || !IsPlain(value, inFlow)) return YamlScalar.SingleQuoted(value);
        return value;
    }

    /// A key, plain where it can be.
    public static string Key(string key, bool inFlow) => Inline(key, ScalarStyle.Plain, inFlow);

    /// Where `text` has half of a UTF-16 surrogate pair without the other, or -1. A UTF-8 file can't hold one (the
    /// encoder writes U+FFFD in its place), so the saved bytes wouldn't be the text, and the editor refuses it.
    public static int LoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            else if (char.IsSurrogate(text[i])) return i;
        }

        return -1;
    }

    /// The reason for refusing text that LoneSurrogate finds half a pair in; `what` names the text.
    public static string LoneSurrogateReason(string what) =>
        $"{what} holds half of a character (an unpaired UTF-16 surrogate), which a UTF-8 file can't hold.";

    /// True when YAML reads `value`, written plain, back as the same text: in a mapping's value or a list's item, in
    /// block style or, with `inFlow`, inside `{ }` or `[ ]`. Conservative, as YamlScalar is: a value quoted that
    /// didn't need it reads the same.
    public static bool IsPlain(string value, bool inFlow)
    {
        if (value.Length == 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])) return false;
        if (value.Any(YamlScalar.NeedsEscape) || value is "---" or "...") return false;
        if (Indicators.Contains(value[0])
            && !(value[0] is '-' or '?' or ':' && value.Length > 1 && value[1] != ' ' && !FlowIndicators.Contains(value[1])))
            return false;
        if (value.Contains(": ", StringComparison.Ordinal) || value.Contains(" #", StringComparison.Ordinal) || value.EndsWith(':'))
            return false;
        return !inFlow || (value.IndexOfAny([.. FlowIndicators]) < 0 && !value.Contains(':'));
    }

    /// The value as a literal (`|`) or folded (`>`) block, for a block value, or null when a block can't hold it: a
    /// character that needs an escape, a space starting its first line (that would need an indentation indicator),
    /// a line of spaces only, or more than one line break at the end. A folded block keeps `>` for one line of text
    /// and becomes literal for more, since folding would join them. `rest` is what followed the old indicator on its
    /// line (a comment); `indent` goes before each line of text.
    public static string? Block(string value, ScalarStyle style, string rest, string indent, string eol)
    {
        if (value.Any(c => c != '\n' && YamlScalar.NeedsEscape(c))) return null;

        var body = value.EndsWith('\n') ? value[..^1] : value;
        var lines = body.Split('\n');
        if (body.EndsWith('\n') || lines.FirstOrDefault(l => l.Length > 0) is not { } first || first[0] == ' '
            || lines.Any(l => l.Length > 0 && l.Trim().Length == 0))
            return null;

        var indicator = style == ScalarStyle.Folded && lines.Length == 1 ? ">" : "|";
        var chomp = value.EndsWith('\n') ? "" : "-";
        return indicator + chomp + rest + eol + string.Join(eol, lines.Select(l => l.Length == 0 ? "" : indent + l));
    }
}
