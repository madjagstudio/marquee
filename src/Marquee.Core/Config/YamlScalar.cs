using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;

namespace Marquee.Core.Config;

/// A plain string written as a YAML value, for ConfigEditor. It stays a bare word when that is safe, and is quoted
/// otherwise, in the existing value's quoting where that can hold it.
static class YamlScalar
{
    /// Words that are safe unquoted anywhere: a letter or underscore first (no number, no YAML indicator), then
    /// letters, digits, `_`, `.` and `-`. Anything else is quoted. Conservative on purpose: quoting a value that didn't
    /// need it is harmless; leaving one plain that did would change its meaning or break the file.
    static readonly Regex PlainWord = new("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);

    /// Plain words YAML's core schema reads as booleans or null. `on`, `off`, `yes` and `no` (booleans only in YAML
    /// 1.1) stay plain: config.yaml uses on and off as words (`providers: { claude: on }`, `handover: off`) and Marquee's
    /// reader takes them as words.
    static readonly string[] CoreSchemaWords = ["true", "True", "TRUE", "false", "False", "FALSE", "null", "Null", "NULL"];

    /// The value as YAML text, in the existing value's quoting where it can hold it. A value with a line break or
    /// control character needs double quotes and escapes; otherwise one that isn't a safe plain word gets single
    /// quotes, which keep Windows paths' backslashes as they are.
    public static string Render(string value, ScalarStyle style = ScalarStyle.Plain)
    {
        if (style == ScalarStyle.DoubleQuoted || value.Any(NeedsEscape)) return DoubleQuoted(value);
        if (style == ScalarStyle.SingleQuoted || !IsPlainWord(value)) return SingleQuoted(value);
        return value;
    }

    static bool IsPlainWord(string value) => PlainWord.IsMatch(value) && !CoreSchemaWords.Contains(value);

    const char LineSeparator = (char)0x2028, ParagraphSeparator = (char)0x2029;

    /// Control characters, and the two Unicode separators YAML treats as line breaks.
    static bool NeedsEscape(char c) => char.IsControl(c) || c is LineSeparator or ParagraphSeparator;

    static string SingleQuoted(string value) => $"'{value.Replace("'", "''")}'";

    static string DoubleQuoted(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var c in value)
        {
            if (c is '\\' or '"') text.Append('\\').Append(c);
            else if (NeedsEscape(c)) text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else text.Append(c);
        }

        return text.Append('"').ToString();
    }
}
