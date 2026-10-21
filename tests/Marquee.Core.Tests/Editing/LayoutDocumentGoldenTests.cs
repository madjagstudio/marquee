using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Tests.Editing;

/// Golden files: Golden/NAME.txt lists edits of Fixtures/NAME.yaml, each with the change it must make, as the lines it
/// replaces (`-`) and the lines it writes (`+`) from the first line that differs (`@@ line N`), or the reason it is
/// refused. Every character outside those lines must be unchanged (Edits checks the whole text), and each edit runs on
/// LF and on CRLF text. The four examples are copies, so a change to an example doesn't change these.
///
/// An edit is written `=== OP PATH ARGUMENTS`:
/// - `set PATH VALUE`
/// - `replace PATH YAML`
/// - `add PATH KEY YAML`, with `.` for the top of the file
/// - `remove PATH`
/// - `insert LIST INDEX YAML`
/// - `remove-item ITEM`
/// - `move ITEM LIST INDEX`
/// - `wrap LIST INDEX,INDEX… YAML`, the YAML being the group's header
/// - `unwrap ITEM`
///
/// The value or YAML is the rest of the line, with `\n` for a line break and `\\` for a backslash. A golden file that
/// doesn't match writes NAME.received.txt beside it (git-ignored) and fails; look at it, and if it is right, rename it
/// to NAME.txt.
public class LayoutDocumentGoldenTests
{
    public static TheoryData<string> Fixtures => new()
    {
        "example-agents", "example-landscape", "example-portrait", "example-showcase",
        "comments", "mixed", "quoting", "empty", "anchors",
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Each_edit_makes_its_expected_change(string name)
    {
        var yaml = File.ReadAllText(Path.Combine(Folder(), "Fixtures", name + ".yaml")).Replace("\r\n", "\n");
        var golden = Path.Combine(Folder(), "Golden", name + ".txt");
        var received = Path.Combine(Folder(), "Golden", name + ".received.txt");
        Assert.True(File.Exists(golden), $"No golden file {golden}.");

        var expected = File.ReadAllText(golden).Replace("\r\n", "\n");
        var actual = Run(expected, yaml, isTheme: name.StartsWith("example-", StringComparison.Ordinal));
        if (actual != expected)
        {
            File.WriteAllText(received, actual);
            Assert.Fail($"Golden file {name}.txt doesn't match: compare {received} with it, and if it is right, rename it.\n\n" +
                FirstDifference(expected, actual));
        }

        File.Delete(received);
    }

    static string Folder([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

    /// The golden file's text with each edit's result as it is now.
    static string Run(string golden, string yaml, bool isTheme)
    {
        var text = new StringBuilder();
        var lines = golden.TrimEnd('\n').Split('\n');
        var i = 0;
        for (; i < lines.Length && !lines[i].StartsWith("=== ", StringComparison.Ordinal); i++)
            text.Append(lines[i]).Append('\n');

        for (; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("=== ", StringComparison.Ordinal)) continue;
            text.Append(lines[i]).Append('\n').Append(Result(yaml, lines[i][4..], isTheme)).Append('\n');
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }

    static string Result(string yaml, string operation, bool isTheme)
    {
        var edit = Parse(operation);
        try
        {
            edit(LayoutDocument.Open(yaml));
        }
        catch (LayoutEditException ex)
        {
            Edits.Refused(yaml, edit);
            return $"refused: {ex.Reason}\n";
        }

        var after = Edits.Apply(yaml, edit);
        if (isTheme) LayoutParser.Parse(after);
        return Hunk(yaml, after);
    }

    static Func<LayoutDocument, LayoutDocument> Parse(string operation)
    {
        var words = operation.Split(' ', 3);
        var path = PathOf(words[1]);
        var rest = words.Length > 2 ? Unescape(words[2]) : "";
        var (first, after) = rest.Split(' ', 2) is [var a, var b] ? (a, b) : (rest, "");
        return words[0] switch
        {
            "set" => d => d.SetScalar(path, rest),
            "replace" => d => d.ReplaceValue(path, rest),
            "add" => d => d.AddKey(path, first, after),
            "remove" => d => d.RemoveKey(path),
            "insert" => d => d.InsertItem(path, Number(first), after),
            "remove-item" => d => d.RemoveItem(path),
            "move" => d => d.MoveItem(path, PathOf(first), Number(after)),
            "wrap" => d => d.WrapInGroup(path, first.Split(',').Select(Number).ToList(), after),
            "unwrap" => d => d.UnwrapGroup(path),
            _ => throw new FormatException($"Unknown golden operation '{operation}'"),
        };
    }

    static YamlPath PathOf(string text) => text == "." ? YamlPath.Root : YamlPath.Parse(text);

    static int Number(string text) => int.Parse(text, CultureInfo.InvariantCulture);

    static string Unescape(string text) => text.Replace("\\\\", "\0").Replace("\\n", "\n").Replace("\0", "\\");

    /// The lines that changed: from the first that differs to the last, before (`-`) and after (`+`).
    static string Hunk(string before, string after)
    {
        if (before == after) return "(no change)\n";
        var (a, b) = (before.Split('\n'), after.Split('\n'));
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start]) start++;
        var end = 0;
        while (end < a.Length - start && end < b.Length - start && a[^(end + 1)] == b[^(end + 1)]) end++;

        var hunk = new StringBuilder($"@@ line {start + 1}\n");
        foreach (var line in a[start..^end]) hunk.Append('-').Append(line).Append('\n');
        foreach (var line in b[start..^end]) hunk.Append('+').Append(line).Append('\n');
        return hunk.ToString();
    }

    static string FirstDifference(string expected, string actual)
    {
        var (e, a) = (expected.Split('\n'), actual.Split('\n'));
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var (x, y) = (i < e.Length ? e[i] : "(end)", i < a.Length ? a[i] : "(end)");
            if (x != y) return $"line {i + 1}\nexpected: {x}\nactual:   {y}";
        }

        return "";
    }
}
