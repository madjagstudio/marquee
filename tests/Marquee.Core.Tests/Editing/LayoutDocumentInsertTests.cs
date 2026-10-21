using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// InsertItem. Each case runs on LF and CRLF text (Edits).
public class LayoutDocumentInsertTests
{
    static string Insert(string yaml, string list, int index, string item) =>
        Edits.Apply(yaml, d => d.InsertItem(YamlPath.Parse(list), index, item));

    [Fact]
    public void Inserts_a_block_item_among_block_items_at_the_lists_indentation() =>
        Assert.Equal(
            "widgets:\n  - type: text\n    at: [0, 0]\n  - type: bar\n    at: [1, 1]\n",
            Insert("widgets:\n  - type: text\n    at: [0, 0]\n", "widgets", 1, "type: bar\nat: [1, 1]"));

    [Fact]
    public void Writes_a_one_liner_between_one_liners() =>
        Assert.Equal(
            "widgets:\n  - { type: a }\n  - { type: c, at: [0, 0] }\n  - { type: b }\n",
            Insert("widgets:\n  - { type: a }\n  - { type: b }\n", "widgets", 1, "type: c\nat: [0, 0]"));

    [Fact]
    public void Writes_a_block_item_next_to_a_block_item() =>
        Assert.Equal(
            "widgets:\n  - type: b\n    at: [1, 2]\n    style: { size: 3 }\n  - type: a\n    at: [0, 0]\n",
            Insert("widgets:\n  - type: a\n    at: [0, 0]\n", "widgets", 0, "{ type: b, at: [1, 2], style: { size: 3 } }"));

    [Fact]
    public void Writes_a_block_item_between_a_block_item_and_a_one_liner() =>
        Assert.Equal(
            "w:\n  - type: a\n    at: [0, 0]\n  - type: c\n  - { type: b }\n",
            Insert("w:\n  - type: a\n    at: [0, 0]\n  - { type: b }\n", "w", 1, "{ type: c }"));

    [Fact]
    public void Writes_an_item_with_rules_in_block_style_even_among_one_liners() =>
        Assert.Equal(
            "w:\n  - { type: a }\n  - type: bar\n    when:\n      - { above: 9, style: hot }\n",
            Insert("w:\n  - { type: a }\n", "w", 1, "{ type: bar, when: [{ above: 9, style: hot }] }"));

    [Fact]
    public void Writes_a_one_liner_in_the_themes_quoting() =>
        Assert.Equal(
            "w:\n  - { type: a }\n  - { type: value, format: \"{0:0}°C\", text: it's }\n",
            Insert("w:\n  - { type: a }\n", "w", 1, "type: value\nformat: '{0:0}°C'\ntext: it's"));

    [Theory]
    [InlineData(0, "widgets:\n  - { type: n }\n  # about a\n  - { type: a }\n    # inside a\n  # about b\n  - { type: b }\n    # inside b\n# the end\n")]
    [InlineData(1, "widgets:\n  # about a\n  - { type: a }\n    # inside a\n  - { type: n }\n  # about b\n  - { type: b }\n    # inside b\n# the end\n")]
    [InlineData(2, "widgets:\n  # about a\n  - { type: a }\n    # inside a\n  # about b\n  - { type: b }\n    # inside b\n  - { type: n }\n# the end\n")]
    public void Goes_above_the_comments_that_describe_the_next_item_and_below_those_inside_the_last(int index, string after) =>
        Assert.Equal(after, Insert(
            "widgets:\n  # about a\n  - { type: a }\n    # inside a\n  # about b\n  - { type: b }\n    # inside b\n# the end\n",
            "widgets", index, "{ type: n }"));

    [Theory]
    [InlineData("widgets: []\n", "type: text\nat: [0, 0]", "widgets:\n  - type: text\n    at: [0, 0]\n")]
    [InlineData("widgets: [ ]   # none yet\nnext: 1\n", "{ type: text }", "widgets:   # none yet\n  - { type: text }\nnext: 1\n")]
    [InlineData("widgets:\n", "{ type: text }", "widgets:\n  - { type: text }\n")]
    [InlineData("widgets:\nnext: 1", "{ type: text }", "widgets:\n  - { type: text }\nnext: 1")]
    [InlineData("at: []\n", "5", "at: [5]\n")]
    [InlineData("at: [ ]\n", "5", "at: [ 5 ]\n")]
    public void Inserts_into_an_empty_list(string before, string item, string after) =>
        Assert.Equal(after, Insert(before, before.StartsWith("at") ? "at" : "widgets", 0, item));

    [Fact]
    public void An_empty_value_becomes_a_block_list_under_its_key_past_its_comment() =>
        Assert.Equal(
            "g:\n  children:   # none\n    - type: a\n      size: [1, 1]\n  at: [0, 0]\n",
            Insert("g:\n  children:   # none\n  at: [0, 0]\n", "g.children", 0, "type: a\nsize: [1, 1]"));

    [Theory]
    [InlineData("a: { b: }\n", "a.b", "a: { b: [x] }\n")]
    [InlineData("a: { b: [] }\n", "a.b", "a: { b: [x] }\n")]
    [InlineData("- []\n", "[0]", "- [x]\n")]
    public void An_empty_list_inside_braces_or_a_list_takes_the_item_in_brackets(string before, string list, string after) =>
        Assert.Equal(after, Insert(before, list, 0, "x"));

    [Fact]
    public void A_new_list_follows_the_files_list_indentation() =>
        Assert.Equal("c:\n- 1\nd:\n- { a: 1 }\n", Insert("c:\n- 1\nd: []\n", "d", 0, "{ a: 1 }"));

    [Theory]
    [InlineData("at: [1, 2]\n", 0, "at: [0, 1, 2]\n")]
    [InlineData("at: [1, 2]\n", 1, "at: [1, 0, 2]\n")]
    [InlineData("at: [1, 2]\n", 2, "at: [1, 2, 0]\n")]
    [InlineData("at: [1,2]\n", 2, "at: [1,2,0]\n")]
    [InlineData("at: [1, 2, ]\n", 2, "at: [1, 2, 0, ]\n")]
    [InlineData("at: [\n    1,\n    2\n  ]\n", 0, "at: [\n    0,\n    1,\n    2\n  ]\n")]
    [InlineData("at: [\n    1,\n    2\n  ]\n", 2, "at: [\n    1,\n    2,\n    0\n  ]\n")]
    public void Inserts_into_a_flow_list_with_its_separator(string before, int index, string after) =>
        Assert.Equal(after, Insert(before, "at", index, "0"));

    [Fact]
    public void Writes_a_block_item_in_flow_style_inside_brackets() =>
        Assert.Equal("l: [{ a: 1 }, { a: 2, b: [3] }]\n", Insert("l: [{ a: 1 }]\n", "l", 1, "a: 2\nb:\n  - 3"));

    [Fact]
    public void Inserts_at_the_end_of_a_file_without_a_last_line_break() =>
        Assert.Equal("w:\n  - a\n  - b\n", Insert("w:\n  - a", "w", 1, "b"));

    [Fact]
    public void Keeps_the_items_own_lines_and_comments_when_written_as_given() =>
        Assert.Equal(
            "w:\n  - type: a\n  - type: b   # new\n    text: |\n      one\n      two\n",
            Insert("w:\n  - type: a\n", "w", 1, "type: b   # new\ntext: |\n  one\n  two\n"));

    [Theory]
    [InlineData("at: [1]\n", "at", 2, "at has 1 item, so a new one goes at 0 to 1, not 2.")]
    [InlineData("a: 1\n", "a", 0, "a isn't a list, so it can't take an item.")]
    [InlineData("a: { b: 1 }\n", "a", 0, "a isn't a list, so it can't take an item.")]
    [InlineData("x: 1\n", "a", 0, "a isn't in the file.")]
    public void Refuses_what_it_cant_insert(string yaml, string list, int index, string reason) =>
        Assert.Equal(reason, Edits.Refused(yaml, d => d.InsertItem(YamlPath.Parse(list), index, "{ type: a }")).Reason);

    [Fact]
    public void Refuses_a_list_reached_through_an_alias() =>
        Assert.Contains("anchor", Edits.Refused("a: &l [1]\nb: *l\n", d => d.InsertItem(YamlPath.Parse("b"), 0, "2")).Reason);

    [Fact]
    public void Inserts_beside_an_anchored_item() =>
        Assert.Equal("l:\n  - 0\n  - &x 1\nm: *x\n", Insert("l:\n  - &x 1\nm: *x\n", "l", 0, "0"));

    /// YamlWriter writes values without tags, so an item it has to restyle can't keep one: the edit is refused rather
    /// than written without it.
    [Theory]
    [InlineData("k: []\n", "k", 0, "!bang")]
    [InlineData("w:\n  - { a: 1 }\n", "w", 1, "!!map\nb: 2")]
    [InlineData("w:\n  - a: 1\n    c: 3\n", "w", 1, "!m { b: 2 }")]
    public void An_item_whose_tag_its_restyling_would_drop_is_refused(string yaml, string list, int index, string item) =>
        Edits.Refused(yaml, d => d.InsertItem(YamlPath.Parse(list), index, item));

    [Fact]
    public void An_item_written_as_given_keeps_its_tag() =>
        Assert.Equal("w:\n  - a\n  - !t b\n", Insert("w:\n  - a\n", "w", 1, "!t b"));

    [Fact]
    public void Comment_lines_an_item_starts_with_go_above_its_dash() =>
        Assert.Equal("w:\n  - type: a\n  # new\n  - type: b\n", Insert("w:\n  - type: a\n", "w", 1, "# new\ntype: b"));

    static string InsertAsWritten(string yaml, string list, int index, string item) =>
        Edits.Apply(yaml, d => d.InsertItem(YamlPath.Parse(list), index, item, asWritten: true));

    [Theory]
    [InlineData(1, "w:\n  - type: a\n    at: [0, 0]\n  - { type: b, text: 'B # not a comment' }   # keep me\n")]
    [InlineData(0, "w:\n  - { type: b, text: 'B # not a comment' }   # keep me\n  - type: a\n    at: [0, 0]\n")]
    public void As_written_a_one_liner_keeps_its_line_quoting_and_comment_beside_a_block_item(int index, string after) =>
        Assert.Equal(after, InsertAsWritten("w:\n  - type: a\n    at: [0, 0]\n", "w", index, "{ type: b, text: 'B # not a comment' }   # keep me"));

    [Fact]
    public void As_written_a_block_item_stays_in_block_style_among_one_liners() =>
        Assert.Equal(
            "w:\n  - { type: a }\n  # about b\n  - type: b\n    text: 'it''s'   # quoted\n  - { type: c }\n",
            InsertAsWritten("w:\n  - { type: a }\n  - { type: c }\n", "w", 1, "# about b\ntype: b\ntext: 'it''s'   # quoted"));

    [Fact]
    public void As_written_a_tagged_item_keeps_its_tag() =>
        Assert.Equal("w:\n  - a: 1\n    c: 3\n  - !m { b: 2 }\n", InsertAsWritten("w:\n  - a: 1\n    c: 3\n", "w", 1, "!m { b: 2 }"));

    [Fact]
    public void As_written_inside_brackets_is_still_one_line() =>
        Assert.Equal("w: [{ a: 1 }, { b: 2 }]\n", InsertAsWritten("w: [{ a: 1 }]\n", "w", 1, "{ b: 2 }"));
}
