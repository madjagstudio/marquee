using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// RemoveItem. Each case runs on LF and CRLF text (Edits).
public class LayoutDocumentRemoveItemTests
{
    const string Commented =
        "widgets:\n  # about a\n  - { type: a }\n    # inside a\n  # about b\n  - type: b\n    at: [0, 0]   # here\n    # inside b\n# the end\n";

    static string Remove(string yaml, string item) => Edits.Apply(yaml, d => d.RemoveItem(YamlPath.Parse(item)));

    [Fact]
    public void Removes_an_item_with_the_comments_above_it_and_those_inside_it() =>
        Assert.Equal(
            "widgets:\n  # about b\n  - type: b\n    at: [0, 0]   # here\n    # inside b\n# the end\n",
            Remove(Commented, "widgets[0]"));

    [Fact]
    public void Removes_the_last_item_and_leaves_the_comments_after_the_list() =>
        Assert.Equal("widgets:\n  # about a\n  - { type: a }\n    # inside a\n# the end\n", Remove(Commented, "widgets[1]"));

    [Fact]
    public void Removing_the_only_item_leaves_an_empty_list_and_the_keys_comment() =>
        Assert.Equal(
            "widgets: []   # all of them\nnext: 1\n",
            Remove("widgets:   # all of them\n  # the one\n  - type: a\n    at: [0, 0]\nnext: 1\n", "widgets[0]"));

    [Fact]
    public void Removing_the_only_child_leaves_the_group_with_an_empty_list() =>
        Assert.Equal(
            "w:\n  - type: group\n    children: []\n  - { type: c }\n",
            Remove("w:\n  - type: group\n    children:\n      - { type: a }\n  - { type: c }\n", "w[0].children[0]"));

    [Theory]
    [InlineData("w:\n  - a\n\n  - b\n\n  - c\n", "w[1]", "w:\n  - a\n\n  - c\n")]
    [InlineData("w:\n  - a\n\n  - b\n", "w[1]", "w:\n  - a\n")]
    [InlineData("w:\n  - a\n  - b\n\nnext: 1\n", "w[1]", "w:\n  - a\n\nnext: 1\n")]
    public void Keeps_one_blank_line_between_the_neighbours(string before, string item, string after) =>
        Assert.Equal(after, Remove(before, item));

    [Theory]
    [InlineData("at: [1, 2, 3]\n", "at[0]", "at: [2, 3]\n")]
    [InlineData("at: [1, 2, 3]\n", "at[1]", "at: [1, 3]\n")]
    [InlineData("at: [1, 2, 3]\n", "at[2]", "at: [1, 2]\n")]
    [InlineData("at: [1]\n", "at[0]", "at: []\n")]
    [InlineData("at: [ 1 ]\n", "at[0]", "at: []\n")]
    [InlineData("at: [\n    1,\n    2\n  ]\n", "at[1]", "at: [\n    1\n  ]\n")]
    public void Removes_an_item_from_a_flow_list_with_its_comma(string before, string item, string after) =>
        Assert.Equal(after, Remove(before, item));

    [Theory]
    [InlineData("a: &x 1\nl:\n  - *x\n  - 2\n", "l[0]", "anchor")]
    [InlineData("l:\n  - &x 1\nm: *x\n", "l[0]", "anchor")]
    [InlineData("a: { b: 1 }\n", "a.b", "isn't an item of a list")]
    [InlineData("l: [1]\n", "l[1]", "isn't in the file")]
    public void Refuses_what_it_cant_remove(string yaml, string item, string reason) =>
        Assert.Contains(reason, Edits.Refused(yaml, d => d.RemoveItem(YamlPath.Parse(item))).Reason);
}
