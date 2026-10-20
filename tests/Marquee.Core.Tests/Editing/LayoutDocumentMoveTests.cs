using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// MoveItem. Each case runs on LF and CRLF text (Edits).
public class LayoutDocumentMoveTests
{
    const string Three = "w:\n  - { type: a }\n  - type: b\n    at: [0, 0]\n  - { type: c }\n";

    static string Move(string yaml, string item, string list, int index) =>
        Edits.Apply(yaml, d => d.MoveItem(YamlPath.Parse(item), YamlPath.Parse(list), index));

    [Theory]
    [InlineData("w[0]", 2, "w:\n  - type: b\n    at: [0, 0]\n  - { type: c }\n  - { type: a }\n")]
    [InlineData("w[0]", 1, "w:\n  - type: b\n    at: [0, 0]\n  - { type: a }\n  - { type: c }\n")]
    [InlineData("w[2]", 0, "w:\n  - { type: c }\n  - { type: a }\n  - type: b\n    at: [0, 0]\n")]
    [InlineData("w[1]", 0, "w:\n  - type: b\n    at: [0, 0]\n  - { type: a }\n  - { type: c }\n")]
    public void Reorders_a_list(string item, int index, string after) => Assert.Equal(after, Move(Three, item, "w", index));

    [Fact]
    public void Moving_an_item_to_its_own_place_changes_nothing()
    {
        var document = LayoutDocument.Open(Three);

        Assert.Same(document, document.MoveItem(YamlPath.Parse("w[1]"), YamlPath.Parse("w"), 1));
    }

    [Theory]
    [InlineData(1, "w:\n  - type: group\n    children:\n      - { type: a }\n      # the bee\n      - type: b   # bee\n        at: [0, 0]\n")]
    [InlineData(0, "w:\n  - type: group\n    children:\n      # the bee\n      - type: b   # bee\n        at: [0, 0]\n      - { type: a }\n")]
    public void Moves_an_item_into_a_group_one_level_deeper(int index, string after) =>
        Assert.Equal(after, Move(
            "w:\n  - type: group\n    children:\n      - { type: a }\n  # the bee\n  - type: b   # bee\n    at: [0, 0]\n",
            "w[1]", "w[0].children", index));

    [Fact]
    public void Moves_an_item_out_of_a_group_one_level_up() =>
        Assert.Equal(
            "w:\n  - type: group\n    children:\n      - { type: b }\n  # the a\n  - type: a\n    text: |\n      one\n      two\n  - { type: c }\n",
            Move("w:\n  - type: group\n    children:\n      # the a\n      - type: a\n        text: |\n          one\n          two\n      - { type: b }\n  - { type: c }\n",
                "w[0].children[0]", "w", 1));

    [Fact]
    public void Moving_the_only_child_out_leaves_an_empty_list() =>
        Assert.Equal(
            "w:\n  - { type: a }\n  - type: group\n    children: []\n  - { type: c }\n",
            Move("w:\n  - type: group\n    children:\n      - { type: a }\n  - { type: c }\n", "w[0].children[0]", "w", 0));

    [Fact]
    public void Moves_an_item_into_an_empty_group() =>
        Assert.Equal(
            "w:\n  - type: group\n    children:\n      - { type: a }\n",
            Move("w:\n  - type: group\n    children: []\n  - { type: a }\n", "w[1]", "w[0].children", 0));

    [Theory]
    [InlineData("a:\n  # c1\n  - 1   # c2\n  - 3\nb: [2]\n", "a[0]", "b")]
    [InlineData("a:\n  - 3\n  - 1   # c2\nb: [2]\n", "a[1]", "b")]
    [InlineData("a:\n  - 3\n  - 1\n    # inside\nb: { c: }\n", "a[1]", "b.c")]
    [InlineData("widgets:\n  # The title\n  - { type: text, text: A }   # big\n  - { type: group, children: [] }\n", "widgets[0]", "widgets[1].children")]
    public void An_item_with_comments_does_not_go_inside_brackets(string yaml, string item, string list)
    {
        var ex = Edits.Refused(yaml, d => d.MoveItem(YamlPath.Parse(item), YamlPath.Parse(list), 0));

        Assert.Contains("comment", ex.Reason);
    }

    [Theory]
    [InlineData("a:\n  # c1\n  - 1   # c2\n  - 3\nb: []   # empty\n", "a:\n  - 3\nb:   # empty\n  # c1\n  - 1   # c2\n")]
    [InlineData("children:\n  - a  #b\nempty_list: []\nkey: value\n", "children: []\nempty_list:\n  - a  #b\nkey: value\n")]
    public void An_item_with_comments_makes_an_empty_list_under_a_key_a_block_list(string yaml, string after) =>
        Assert.Equal(after, Move(yaml, yaml.StartsWith('a') ? "a[0]" : "children[0]", yaml.StartsWith('a') ? "b" : "empty_list", 0));

    [Fact]
    public void An_item_without_comments_still_goes_inside_brackets() =>
        Assert.Equal("a:\n  - 3\nb: [1, 2]\n", Move("a:\n  - 1\n  - 3\nb: [2]\n", "a[0]", "b", 0));

    [Fact]
    public void Moves_the_last_child_out_past_a_blank_line() =>
        Assert.Equal(
            "w:\n  - type: group\n    children:\n      - { type: a }\n\n  - { type: b }\n",
            Move("w:\n  - type: group\n    children:\n      - { type: a }\n\n      - { type: b }\n\n", "w[0].children[1]", "w", 1));

    [Theory]
    [InlineData("at[0]", 2, "at: [2, 3, 1]\n")]
    [InlineData("at[2]", 0, "at: [3, 1, 2]\n")]
    [InlineData("at[0]", 1, "at: [2, 1, 3]\n")]
    public void Reorders_a_flow_list(string item, int index, string after) =>
        Assert.Equal(after, Move("at: [1, 2, 3]\n", item, "at", index));

    [Fact]
    public void Moves_a_one_line_item_into_brackets() =>
        Assert.Equal("w: []\nl: [1, { type: a }]\n", Move("w:\n  - { type: a }\nl: [1]\n", "w[0]", "l", 1));

    [Fact]
    public void Moves_an_item_out_of_brackets_into_a_block_list() =>
        Assert.Equal("w:\n  - { type: a }\n  - 2\nl: [1]\n", Move("w:\n  - { type: a }\nl: [1, 2]\n", "l[1]", "w", 1));

    [Theory]
    [InlineData("w[0]", "w[0].children", 0, "can't go inside itself")]
    [InlineData("w[0]", "w[0].children[0]", 0, "can't go inside itself")]
    [InlineData("w[1]", "l", 0, "over several lines")]
    [InlineData("w[0]", "w", 3, "w has 3 items, so an item moved in it goes at 0 to 2, not 3.")]
    [InlineData("w[0]", "w[1].type", 0, "w[1].type isn't a list")]
    [InlineData("w.type", "w", 0, "isn't an item of a list")]
    public void Refuses_what_it_cant_move(string item, string list, int index, string reason) =>
        Assert.Contains(reason, Edits.Refused(
            "w:\n  - type: group\n    children:\n      - { type: x }\n  - type: b\n    at: [0, 0]\n  - { type: c }\nl: [1]\n",
            d => d.MoveItem(YamlPath.Parse(item), YamlPath.Parse(list), index)).Reason);
}
