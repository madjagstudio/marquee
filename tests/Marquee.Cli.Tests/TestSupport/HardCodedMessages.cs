using System.Text.RegularExpressions;

namespace Marquee.Cli.Tests.TestSupport;

/// Finds console writes whose argument starts with a string literal ("…", $"…", @"…", """…""", $"""…"""): the way a
/// message gets past Strings.resx and can never be translated.
static class HardCodedMessages
{
    // `\s*` crosses line breaks, so a literal on the line after `Console.Error.WriteLine(` is still caught.
    static readonly Regex Call = new(
        @"(?:\bConsole(?:\.Out|\.Error)?|\.Error)\.Write(?:Line)?\(\s*(?:\$*@?|@\$*)""",
        RegexOptions.CultureInvariant);

    /// Each hit's 1-based line number and that line's trimmed text.
    public static IReadOnlyList<(int Line, string Text)> Find(string source)
    {
        var text = source.ReplaceLineEndings("\n");
        var lines = text.Split('\n');
        return Call.Matches(text)
            .Select(match =>
            {
                var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                return (line, lines[line - 1].Trim());
            })
            .ToList();
    }
}
