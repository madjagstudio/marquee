using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// ItemYaml: an item's own YAML text, comments and quoting included, for copy, paste and duplicate.
public class LayoutDocumentItemYamlTests
{
    const string Widgets =
        "widgets:\n" +
        "  # The title\n" +
        "  - type: text   # big\n" +
        "    # inside\n" +
        "    text: |\n" +
        "      a\n" +
        "      b\n" +
        "    color: 'grey'\n" +
        "  - { type: c }\n";

    static string ItemYaml(string yaml, string item) => LayoutDocument.Open(yaml).ItemYaml(YamlPath.Parse(item));

    [Fact]
    public void A_block_item_is_its_lines_and_comments_without_its_dash() =>
        Assert.Equal(
            "# The title\ntype: text   # big\n# inside\ntext: |\n  a\n  b\ncolor: 'grey'",
            ItemYaml(Widgets, "widgets[0]"));

    [Fact]
    public void Duplicating_an_item_through_its_text_keeps_its_comments() =>
        Assert.Equal(
            Widgets.Replace("  - { type: c }\n", "") + Widgets[9..].Replace("  - { type: c }\n", "") + "  - { type: c }\n",
            Edits.Apply(Widgets, d => d.InsertItem(YamlPath.Parse("widgets"), 1, d.ItemYaml(YamlPath.Parse("widgets[0]")))));

    [Theory]
    [InlineData("l: [a, { b: 1 }]\n", "l[1]", "{ b: 1 }")]
    [InlineData("l:\n  - 1   # c2\n  - 3\n", "l[0]", "1   # c2")]
    [InlineData("l:\n  -\n    a: 1\n    b: 2\n", "l[0]", "a: 1\nb: 2")]
    [InlineData("l:\r\n  - a: 1\r\n    b: \"x\"\r\n", "l[0]", "a: 1\nb: \"x\"")]
    [InlineData("l:\n- - x\n  - y\n", "l[0]", "- x\n- y")]
    [InlineData("l: [a, [\n    b, c]]\n", "l[1]", "[\n    b, c]")]
    public void An_item_is_its_own_text(string yaml, string item, string expected) =>
        Assert.Equal(expected, ItemYaml(yaml, item));

    [Theory]
    [InlineData("l: [a]\n", "l")]
    [InlineData("l: [a]\n", "l[1]")]
    [InlineData("a: &x 1\nl:\n  - *x\n", "l[0]")]
    [InlineData("l:\n  -   # the item\n    a: 1\n", "l[0]")]
    public void Refuses_what_is_not_an_item_it_can_read(string yaml, string item) =>
        Assert.Throws<LayoutEditException>(() => ItemYaml(yaml, item));
}
