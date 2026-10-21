using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// What LayoutDocument won't open or edit, and that a refused edit changes nothing.
public class LayoutDocumentRefusalTests
{
    [Theory]
    [InlineData("a: 1\na: 2\n", 2)]
    [InlineData("w:\n  - { type: text, type: bar }\n", 2)]
    [InlineData("styles:\n  hot: { color: red }\n  cold: {}\n  hot: {}\n", 4)]
    public void Refuses_to_open_a_file_with_a_duplicate_key(string yaml, int line)
    {
        var ex = Assert.Throws<LayoutEditException>(() => LayoutDocument.Open(yaml));

        Assert.Equal(line, ex.Line);
        Assert.Contains("twice", ex.Reason);
    }

    [Theory]
    [InlineData("a: 1\n---\nb: 2\n")]
    [InlineData("---\na: 1\n---\n")]
    public void Refuses_to_open_a_file_with_more_than_one_document(string yaml)
    {
        var ex = Assert.Throws<LayoutEditException>(() => LayoutDocument.Open(yaml));

        Assert.Contains("more than one", ex.Reason);
    }

    [Theory]
    [InlineData("a: [1, 2\nb: 3\n")]
    [InlineData("a: 1\n  b: 2\n")]
    public void Refuses_to_open_a_file_that_does_not_parse(string yaml)
    {
        var ex = Assert.Throws<LayoutEditException>(() => LayoutDocument.Open(yaml));

        Assert.StartsWith("YAML syntax error", ex.Reason);
    }

    [Fact]
    public void Opens_a_file_with_one_explicit_document()
    {
        var document = LayoutDocument.Open("---\na: 1\n...\n");

        Assert.Equal("---\na: 2\n...\n", document.SetScalar(YamlPath.Parse("a"), "2").Text);
    }

    const string Anchored =
        "styles:\n" +
        "  base: &base { size: 20, color: red }\n" +
        "  gap: &gap 8\n" +
        "widgets:\n" +
        "  - { type: text, at: [*gap, 0], style: *base }\n" +
        "  - type: text\n" +
        "    at: [0, 0]\n" +
        "    style: { size: 12 }\n";

    [Theory]
    [InlineData("styles.base")]
    [InlineData("styles.base.size")]
    [InlineData("styles.gap")]
    [InlineData("widgets[0]")]
    [InlineData("widgets[0].at")]
    [InlineData("widgets[0].at[0]")]
    [InlineData("widgets[0].style")]
    [InlineData("widgets[0].style.color")]
    [InlineData("styles")]
    [InlineData("")]
    public void Anything_with_an_anchor_or_alias_on_it_or_in_it_is_not_editable(string path) =>
        Assert.False(LayoutDocument.Open(Anchored).IsEditable(YamlPath.Parse(path)));

    [Theory]
    [InlineData("widgets[0].type")]
    [InlineData("widgets[0].at[1]")]
    [InlineData("widgets[1]")]
    [InlineData("widgets[1].style.size")]
    public void The_rest_of_the_file_stays_editable(string path) =>
        Assert.True(LayoutDocument.Open(Anchored).IsEditable(YamlPath.Parse(path)));

    [Theory]
    [InlineData("widgets[9]")]
    [InlineData("nothing")]
    public void A_path_that_is_not_in_the_file_neither_exists_nor_is_editable(string path)
    {
        var document = LayoutDocument.Open(Anchored);

        Assert.False(document.Exists(YamlPath.Parse(path)));
        Assert.False(document.IsEditable(YamlPath.Parse(path)));
        Assert.True(document.Exists(YamlPath.Parse("widgets[0].style.color")));
    }

    public static TheoryData<string, Func<LayoutDocument, LayoutDocument>> AnchorEdits => new()
    {
        { "set inside an anchored mapping", d => d.SetScalar(YamlPath.Parse("styles.base.size"), "30") },
        { "set an anchored scalar", d => d.SetScalar(YamlPath.Parse("styles.gap"), "9") },
        { "set an alias", d => d.SetScalar(YamlPath.Parse("widgets[0].at[0]"), "9") },
        { "set through an alias", d => d.SetScalar(YamlPath.Parse("widgets[0].style.size"), "30") },
        { "replace an alias", d => d.ReplaceValue(YamlPath.Parse("widgets[0].style"), "{ size: 30 }") },
        { "replace an anchored node", d => d.ReplaceValue(YamlPath.Parse("styles.base"), "{ size: 30 }") },
        { "replace a value holding an alias", d => d.ReplaceValue(YamlPath.Parse("widgets[0].at"), "[1, 2]") },
        { "add to an anchored mapping", d => d.AddKey(YamlPath.Parse("styles.base"), "weight", "bold") },
        { "add through an alias", d => d.AddKey(YamlPath.Parse("widgets[0].style"), "weight", "bold") },
        { "remove an anchored node", d => d.RemoveKey(YamlPath.Parse("styles.base")) },
        { "remove an alias", d => d.RemoveKey(YamlPath.Parse("widgets[0].style")) },
        { "remove inside an anchored mapping", d => d.RemoveKey(YamlPath.Parse("styles.base.color")) },
        { "remove a key holding an anchor", d => d.RemoveKey(YamlPath.Parse("styles")) },
    };

    [Theory]
    [MemberData(nameof(AnchorEdits))]
    public void Refuses_edits_inside_anchored_or_aliased_nodes(string what, Func<LayoutDocument, LayoutDocument> edit)
    {
        var ex = Edits.Refused(Anchored, edit);

        Assert.True(ex.Reason.Contains("anchor", StringComparison.Ordinal), $"{what}: {ex.Reason}");
    }

    [Fact]
    public void Edits_the_rest_of_a_file_with_anchors()
    {
        var after = Edits.Set(Anchored, "widgets[1].style.size", "14");

        Assert.Equal(Anchored.Replace("style: { size: 12 }", "style: { size: 14 }"), after);
    }

    [Fact]
    public void An_edit_that_would_add_a_duplicate_key_is_refused() =>
        Edits.Refused("a: { b: 1 }\n", d => d.ReplaceValue(YamlPath.Parse("a"), "{ b: 1, b: 2 }"));
}
