using System.Globalization;
using System.Runtime.CompilerServices;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Parsing;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Tests.Editing;

/// What holds for every edit, tried on every single value in each example theme, with LF and CRLF line breaks: the text
/// outside the value is unchanged to the character, and the result parses, with the new value and every other value
/// as it was. The values are random, from a fixed seed so a failure repeats.
public class LayoutDocumentPropertyTests
{
    static readonly string[] Names = ["example-agents", "example-landscape", "example-portrait", "example-showcase"];

    public static TheoryData<string> Examples => new(Names);

    /// Values that test the quoting: YAML indicators, quotes, comments, line breaks, numbers and words YAML reads
    /// specially, spaces at the ends and text that isn't ASCII.
    static readonly string[] AnyValues =
    [
        "0", "-1", "12.5", "1e3", "word", "two words", "CPU: hot", "it's", "say \"hi\"", "#FF0000", "a # b", "a#b",
        "{x}", "[x]", "- x", "-x", "a, b", "", " padded ", "ünï · °C", "line\nbreak", "tab\there", @"C:\path\x", "true",
        "null", "~", "*star", "&amp", "!bang", "%pct", "@at", "`tick`", "?q", ":c", "x:", "---", "'", "\"",
    ];

    [Theory]
    [MemberData(nameof(Examples))]
    public void Setting_any_value_anywhere_changes_only_that_value(string name)
    {
        var random = new Random(Array.IndexOf(Names, name));
        var yaml = Fixture(name);
        var variants = Variants(yaml);
        foreach (var (path, _) in Scalars(yaml))
        {
            var value = AnyValues[random.Next(AnyValues.Length)];
            foreach (var (text, nodes) in variants)
            {
                var after = LayoutDocument.Open(text).SetScalar(path, value).Text;

                AssertOnlyTheValueChanged(text, after, nodes[path]);
                var expected = YamlValues.Read(text);
                expected[path.ToString()] = value;
                Assert.Equal(expected.OrderBy(e => e.Key), YamlValues.Read(after).OrderBy(e => e.Key));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Setting_a_valid_value_anywhere_keeps_the_theme_valid(string name)
    {
        var random = new Random(100 + Array.IndexOf(Names, name));
        var yaml = Fixture(name);
        var variants = Variants(yaml);
        var alternatives = Scalars(yaml).GroupBy(s => LastKey(s.Path))
            .ToDictionary(g => g.Key, g => g.Select(s => s.Node.Value!).Distinct().ToArray());
        var edited = 0;
        foreach (var (path, old) in Scalars(yaml))
        {
            if (Valid(path, old, random, alternatives) is not { } value) continue;
            foreach (var (text, nodes) in variants)
            {
                var after = LayoutDocument.Open(text).SetScalar(path, value).Text;

                AssertOnlyTheValueChanged(text, after, nodes[path]);
                LayoutParser.Parse(after);
            }

            edited++;
        }

        // Only types are left as they were, so most values get a new one.
        Assert.True(edited > Scalars(yaml).Count() * 2 / 3, $"Only {edited} values were edited");
    }

    static string Fixture(string name, [CallerFilePath] string file = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "Fixtures", name + ".yaml")).Replace("\r\n", "\n");

    /// The text with LF line breaks and with CRLF, each with its values by path.
    static (string Text, Dictionary<YamlPath, YamlScalarNode> Nodes)[] Variants(string yaml) =>
        [.. new[] { yaml, Edits.Crlf(yaml) }.Select(text => (text, Scalars(text).ToDictionary(s => s.Path, s => s.Node)))];

    /// Every single value in the file, with its path, in document order. Keys aren't values.
    static IEnumerable<(YamlPath Path, YamlScalarNode Node)> Scalars(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return Walk(stream.Documents[0].RootNode, YamlPath.Root);

        static IEnumerable<(YamlPath, YamlScalarNode)> Walk(YamlNode node, YamlPath path) => node switch
        {
            YamlScalarNode scalar => [(path, scalar)],
            YamlSequenceNode list => list.Children.SelectMany((item, i) => Walk(item, path.Item(i))),
            YamlMappingNode map => map.Children.SelectMany(e => Walk(e.Value, path.Child(((YamlScalarNode)e.Key).Value!))),
            _ => [],
        };
    }

    /// The characters before the value's start and after its end are the same as before the edit.
    static void AssertOnlyTheValueChanged(string before, string after, YamlScalarNode node)
    {
        var (start, end) = ((int)node.Start.Index, (int)node.End.Index);

        Assert.Equal(before[..start], after[..start]);
        Assert.Equal(before[end..], after[(after.Length - (before.Length - end))..]);
    }

    /// A new value LayoutParser accepts where `old` is: a number spelled another way (or, for a position, another
    /// number), another color, other text for a text or a name, or another value the same key has elsewhere in the
    /// file (a style name, a sensor, an alignment). Null for a template, and for a widget's or shape's type, which
    /// decides its other keys.
    static string? Valid(YamlPath path, YamlScalarNode old, Random random, Dictionary<string, string[]> alternatives)
    {
        var text = old.Value!;
        var key = LastKey(path);
        if (text.StartsWith('#') && text.Length is 7 or 9)
            return "#" + string.Concat(Enumerable.Range(0, text.Length - 1).Select(_ => "0123456789ABCDEF"[random.Next(16)]));
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return key == "at"
                ? random.Next(-50, 1000).ToString(CultureInfo.InvariantCulture)
                : number.ToString("0.0##", CultureInfo.InvariantCulture);
        }

        string[] texts = ["CPU LOAD", "it's hot", "a: b", "#1", "say \"hi\"", "ünï · °C", @"C:\x", "- dash", "50%", "x, y"];
        // A text with {...} in it is a template, and a value widget's spans must keep its {0}.
        if (key is "text" or "name") return text.Contains('{') ? null : texts[random.Next(texts.Length)];
        return key is "type" or "shape" ? null : alternatives[key][random.Next(alternatives[key].Length)];
    }

    static string LastKey(YamlPath path) => path.Segments.Last(s => s.IsKey).Key!;
}
