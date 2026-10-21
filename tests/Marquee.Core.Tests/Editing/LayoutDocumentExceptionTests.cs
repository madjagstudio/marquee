using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// The contract every caller relies on: Open and every edit throw LayoutEditException, and nothing else, for text they
/// can't read and edits they won't make, whatever the file holds.
public class LayoutDocumentExceptionTests
{
    /// The edit either works or is refused with LayoutEditException; any other exception fails the test.
    static void OnlyRefuses(string yaml, Func<LayoutDocument, LayoutDocument> edit)
    {
        var document = LayoutDocument.Open(yaml);
        try
        {
            edit(document);
        }
        catch (LayoutEditException)
        {
            Assert.Equal(yaml, document.Text);
        }
    }

    [Theory]
    [InlineData("- :\n")]
    [InlineData("tagged: !!s[tr 30\n")]
    public void Open_refuses_text_that_YamlDotNet_rejects_without_a_position(string yaml)
    {
        var ex = Assert.Throws<LayoutEditException>(() => LayoutDocument.Open(yaml));

        Assert.StartsWith("YAML syntax error", ex.Reason);
        Assert.DoesNotContain("(Parameter", ex.Reason);
    }

    [Theory]
    [InlineData("- :")]
    [InlineData("!!s[tr 30")]
    public void A_new_value_YamlDotNet_rejects_without_a_position_is_refused(string fragment)
    {
        Edits.Refused("a: 1\n", d => d.ReplaceValue(YamlPath.Parse("a"), fragment));
        Assert.Throws<SpliceException>(() => YamlFragment.Parse(fragment));
    }

    [Fact]
    public void An_edit_whose_result_YamlDotNet_rejects_without_a_position_is_refused()
    {
        // `:` is a mapping with an empty key; under `b:` it becomes `  :`, which YamlDotNet can't read.
        Edits.Refused("b: 1\n", d => d.ReplaceValue(YamlPath.Parse("b"), ":"));
        Edits.Refused("w: []\n", d => d.AddKey(YamlPath.Root, "new_key", ":"));
    }

    [Theory]
    [InlineData("a: 1\n? complex\n: v\n", "complex")]
    [InlineData("? complex\n: v\nb: 1\n", "complex")]
    public void Removing_a_complex_key_works_or_is_refused(string yaml, string key) =>
        OnlyRefuses(yaml, d => d.RemoveKey(YamlPath.Parse(key)));

    static string Fixture(string name, [System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "Fixtures", name + ".yaml")).Replace("\r\n", "\n");

    [Fact]
    public void Adding_to_a_mapping_whose_last_value_is_an_alias_is_refused()
    {
        var ex = Edits.Refused(Fixture("anchors"), d => d.AddKey(YamlPath.Parse("widgets[0]"), "k", "1"));

        Assert.Contains("alias", ex.Reason);
    }

    [Theory]
    [InlineData("a:\n  ? x\n  : 1\n", "a", "1")]
    [InlineData("new_key:\n  ?\n", "new_key", "\"quoted\nvalue\"")]
    public void Adding_after_a_complex_key_is_refused(string yaml, string mapping, string value) =>
        Edits.Refused(yaml, d => d.AddKey(YamlPath.Parse(mapping), "k", value));

    [Theory]
    [InlineData("a: 1\n? complex\n: v\n", "a: 1\n")]
    [InlineData("? complex\n: v\nb: 1\n", "b: 1\n")]
    [InlineData("a: 1\n# about it\n? complex\n: v\nb: 1\n", "a: 1\nb: 1\n")]
    public void Removes_a_complex_key_written_with_a_question_mark(string yaml, string after) =>
        Assert.Equal(after, Edits.Remove(yaml, "complex"));

    [Fact]
    public void Inserting_an_item_whose_result_YamlDotNet_rejects_is_refused() =>
        Edits.Refused("children:\nempty_list: []\nkey: value\n", d => d.InsertItem(YamlPath.Parse("children"), 0, ":"));

    /// Texts the splicing finds hard to measure: anchors and aliases, complex keys, tagged empty values, and the shape
    /// YamlDotNet throws ArgumentException for.
    public static TheoryData<string> HardTexts => new()
    {
        Fixture("anchors"),
        "a:\n  ? x\n  : 1\nb: [1, 2]\n",
        "a: 1\n? complex\n: v\nw: []\n",
        "? complex\n: v\nb: 1\n",
        "w: []\nchildren:\nb: { c: , d: [] }\n",
        "l:\n  - a: !t\n\n  # two\n  - b: 1\n  - { type: group, children: [x] }\n",
        "x: &y 1\nl: [*y, [*y], { a: *y }]\nm:\n  - *y\n  - - *y\n",
        "- - a\n  - b\n- {c: d}\n- [e, {f: g}]\n",
    };

    static readonly string[] Fragments = ["x", "{ a: 1 }", ":", "- :", "a: 1\nb: 2", "- a\n- b", "|\n  t", "!t", "[]"];

    /// Every path in the document, by its keys and items.
    static IEnumerable<YamlPath> Paths(LayoutDocument document, YamlPath path)
    {
        if (document.GetNode(path) is not { } node) yield break;
        yield return path;
        var children = node.Kind == LayoutNodeKind.List
            ? Enumerable.Range(0, node.Count).Select(path.Item)
            : node.Keys.Select(path.Child);
        foreach (var child in children.ToList())
            foreach (var inner in Paths(document, child))
                yield return inner;
    }

    [Theory]
    [MemberData(nameof(HardTexts))]
    public void Every_edit_of_a_hard_text_works_or_is_refused(string yaml)
    {
        var document = LayoutDocument.Open(yaml);
        var paths = Paths(document, YamlPath.Root).ToList();
        var lists = paths.Where(p => document.GetNode(p)!.Kind is LayoutNodeKind.List or LayoutNodeKind.Empty).ToList();
        foreach (var path in paths)
        {
            OnlyRefuses(yaml, d => d.SetScalar(path, "x"));
            OnlyRefuses(yaml, d => d.RemoveKey(path));
            OnlyRefuses(yaml, d => d.RemoveItem(path));
            OnlyRefuses(yaml, d => d.UnwrapGroup(path));
            OnlyRefuses(yaml, d => d.WrapInGroup(path, [0], "type: group"));
            foreach (var fragment in Fragments)
            {
                OnlyRefuses(yaml, d => d.ReplaceValue(path, fragment));
                OnlyRefuses(yaml, d => d.AddKey(path, "k", fragment));
                OnlyRefuses(yaml, d => d.InsertItem(path, 0, fragment));
            }

            foreach (var list in lists)
                OnlyRefuses(yaml, d => d.MoveItem(path, list, 0));
        }
    }
}
