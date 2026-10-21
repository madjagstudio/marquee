using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// WrapInGroup and UnwrapGroup. Each case runs on LF and CRLF text (Edits).
public class LayoutDocumentGroupTests
{
    const string Three = "w:\n  - { type: a }\n  # bee\n  - type: b\n    at: [0, 0]\n  - { type: c }\n";

    const string Header = "type: group\nat: [0, 0]\nsize: [10, 10]";

    static string Wrap(string yaml, string list, int[] indexes, string header) =>
        Edits.Apply(yaml, d => d.WrapInGroup(YamlPath.Parse(list), indexes, header));

    static string Unwrap(string yaml, string group) => Edits.Apply(yaml, d => d.UnwrapGroup(YamlPath.Parse(group)));

    [Fact]
    public void Wraps_neighbouring_items_in_a_group_at_the_first_ones_place() =>
        Assert.Equal(
            "w:\n  - type: group\n    at: [0, 0]\n    size: [10, 10]\n    children:\n      - { type: a }\n      # bee\n      - type: b\n        at: [0, 0]\n  - { type: c }\n",
            Wrap(Three, "w", [0, 1], Header));

    [Theory]
    [InlineData(new[] { 0, 2 })]
    [InlineData(new[] { 2, 0 })]
    public void Wraps_items_apart_keeping_their_order(int[] indexes) =>
        Assert.Equal(
            "w:\n  - type: group\n    at: [0, 0]\n    size: [10, 10]\n    children:\n      - { type: a }\n      - { type: c }\n  # bee\n  - type: b\n    at: [0, 0]\n",
            Wrap(Three, "w", indexes, Header));

    [Fact]
    public void Writes_a_flow_header_in_block_style_and_takes_the_items_comment_inside() =>
        Assert.Equal(
            "w:\n  - { type: a }\n  - type: group\n    name: B\n    children:\n      # bee\n      - type: b\n        at: [0, 0]\n  - { type: c }\n",
            Wrap(Three, "w", [1], "{ type: group, name: B }"));

    [Fact]
    public void Wraps_items_of_a_flow_list_on_one_line() =>
        Assert.Equal(
            "l: [{ type: group, children: [{ a: 1 }, { b: 3 }] }, 2]\n",
            Wrap("l: [{ a: 1 }, 2, { b: 3 }]\n", "l", [0, 2], "type: group"));

    [Theory]
    [InlineData(new int[0], Header, "Choose at least one item")]
    [InlineData(new[] { 0, 0 }, Header, "more than once")]
    [InlineData(new[] { 3 }, Header, "w has 3 items, so there is no item 3.")]
    [InlineData(new[] { 0 }, "type: group\nchildren: []", "can't have children")]
    [InlineData(new[] { 0 }, "- group", "must be a mapping")]
    public void Refuses_a_wrap_it_cant_make(int[] indexes, string header, string reason) =>
        Assert.Contains(reason, Edits.Refused(Three, d => d.WrapInGroup(YamlPath.Parse("w"), indexes, header)).Reason);

    [Fact]
    public void Unwraps_a_group_into_its_place_keeping_the_comment_above_it() =>
        Assert.Equal(
            "w:\n  # the panel\n  - { type: a }\n  # bee\n  - type: b\n    at: [1, 1]\n  - { type: c }\n",
            Unwrap("w:\n  # the panel\n  - type: group   # panel\n    at: [0, 0]\n    children:\n      - { type: a }\n      # bee\n      - type: b\n        at: [1, 1]\n  - { type: c }\n", "w[0]"));

    [Fact]
    public void Unwraps_a_one_line_group() =>
        Assert.Equal(
            "w:\n  - { type: a }\n  - { type: b }\n",
            Unwrap("w:\n  - { type: group, children: [{ type: a }, { type: b }] }\n", "w[0]"));

    [Theory]
    [InlineData("w:\n  - { type: group, children: [] }\n  - { type: c }\n", "w:\n  - { type: c }\n")]
    [InlineData("w:\n  - type: group\n    children:\n", "w: []\n")]
    public void Unwrapping_an_empty_group_removes_it(string before, string after) => Assert.Equal(after, Unwrap(before, "w[0]"));

    [Fact]
    public void Unwraps_a_group_in_a_flow_list() =>
        Assert.Equal("l: [1, 2, 3]\n", Unwrap("l: [{ type: group, children: [1, 2] }, 3]\n", "l[0]"));

    [Theory]
    [InlineData("w:\n  - { type: a }\n", "w[0]", "has no children list")]
    [InlineData("w:\n  - { type: g, children: 5 }\n", "w[0]", "has no children list")]
    [InlineData("w: { children: [] }\n", "w", "isn't an item of a list")]
    public void Refuses_what_isnt_a_group_in_a_list(string yaml, string group, string reason) =>
        Assert.Contains(reason, Edits.Refused(yaml, d => d.UnwrapGroup(YamlPath.Parse(group))).Reason);

    [Theory]
    [InlineData("l: [a]\n", "!g { type: group }")]
    [InlineData("l:\n  - a\n", "!g { type: group }")]
    public void A_header_whose_tag_its_restyling_would_drop_is_refused(string yaml, string header) =>
        Edits.Refused(yaml, d => d.WrapInGroup(YamlPath.Parse("l"), [0], header));
}
