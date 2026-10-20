using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// SetScalar and ReplaceValue. Each case runs on LF and CRLF text (Edits).
public class LayoutDocumentScalarTests
{
    [Fact]
    public void Sets_a_block_value_and_keeps_its_comment() =>
        Assert.Equal(
            "canvas:\n  orientation: landscape\n  fps: 24   # smooth\n",
            Edits.Set("canvas:\n  orientation: landscape\n  fps: 30   # smooth\n", "canvas.fps", "24"));

    [Fact]
    public void Sets_a_value_inside_a_one_line_flow_widget() =>
        Assert.Equal(
            "widgets:\n  - { type: bar, at: [120, 326], style: meter }\n",
            Edits.Set("widgets:\n  - { type: bar, at: [100, 326], style: meter }\n", "widgets[0].at[0]", "120"));

    [Fact]
    public void Sets_a_value_in_nested_flow_and_keeps_its_double_quotes() =>
        Assert.Equal(
            "style: { fill: { linear: [\"#36D1DC\", \"#000000\"] } }\n",
            Edits.Set("style: { fill: { linear: [\"#36D1DC\", \"#5B86E5\"] } }\n", "style.fill.linear[1]", "#000000"));

    [Theory]
    [InlineData("a: word\n", "other", "a: other\n")]
    [InlineData("a: word\n", "two words", "a: two words\n")]
    [InlineData("a: word\n", "-12.5", "a: -12.5\n")]
    [InlineData("a: word\n", "true", "a: true\n")]
    [InlineData("a: word\n", "a, b", "a: a, b\n")]
    [InlineData("a: word\n", "a: b", "a: 'a: b'\n")]
    [InlineData("a: word\n", "a #b", "a: 'a #b'\n")]
    [InlineData("a: word\n", "#FF0000", "a: '#FF0000'\n")]
    [InlineData("a: word\n", "{x}", "a: '{x}'\n")]
    [InlineData("a: word\n", "- x", "a: '- x'\n")]
    [InlineData("a: word\n", " x", "a: ' x'\n")]
    [InlineData("a: word\n", "", "a: ''\n")]
    [InlineData("a: word\n", "it's", "a: it's\n")]
    [InlineData("a: word\n", "line\nbreak", "a: \"line\\u000Abreak\"\n")]
    [InlineData("a: [word]\n", "a, b", "a: ['a, b']\n")]
    [InlineData("a: [word]\n", "x]", "a: ['x]']\n")]
    [InlineData("a: { b: word }\n", "a:b", "a: { b: 'a:b' }\n")]
    [InlineData("a: 'word'\n", "it's", "a: 'it''s'\n")]
    [InlineData("a: 'word'\n", "12", "a: '12'\n")]
    [InlineData("a: \"word\"\n", "say \"hi\" \\o/", "a: \"say \\\"hi\\\" \\\\o/\"\n")]
    [InlineData("a: \"word\"\n", "°·ü", "a: \"°·ü\"\n")]
    [InlineData("a: !!str 30\n", "40", "a: !!str 40\n")]
    public void Keeps_the_quoting_style_where_it_can_hold_the_value(string before, string value, string after) =>
        Assert.Equal(after, Edits.Set(before, before.Contains('{') ? "a.b" : before.Contains('[') ? "a[0]" : "a", value));

    [Theory]
    [InlineData("text: |\n  one\n  two\nnext: 1\n", "three\nfour\n", "text: |\n  three\n  four\nnext: 1\n")]
    [InlineData("text: |\n  one\n\nnext: 1\n", "single", "text: |-\n  single\n\nnext: 1\n")]
    [InlineData("text: |-  # a note\n    one\nnext: 1\n", "x\ny", "text: |-  # a note\n    x\n    y\nnext: 1\n")]
    [InlineData("text: >\n  one\n  two\nnext: 1\n", "a b\n", "text: >\n  a b\nnext: 1\n")]
    [InlineData("text: >-\n  one\nnext: 1\n", "a\nb", "text: |-\n  a\n  b\nnext: 1\n")]
    [InlineData("text: |\n  one\nnext: 1\n", " indented", "text: \" indented\"\nnext: 1\n")]
    [InlineData("- |\n  one\n- two\n", "x\n", "- |\n  x\n- two\n")]
    public void A_block_scalar_stays_a_block_where_it_can(string before, string value, string after) =>
        Assert.Equal(after, Edits.Set(before, before.StartsWith('-') ? "[0]" : "text", value));

    [Theory]
    [InlineData("name:\nnext: 1\n", "name", "name: Clock\nnext: 1\n")]
    [InlineData("name:   # what the layers list calls it\n", "name", "name: Clock   # what the layers list calls it\n")]
    [InlineData("name:", "name", "name: Clock")]
    [InlineData("a: { name: , b: 1 }\n", "a.name", "a: { name: Clock, b: 1 }\n")]
    [InlineData("a: { b: 1, name: }\n", "a.name", "a: { b: 1, name: Clock }\n")]
    [InlineData("a: ''\n", "a", "a: 'Clock'\n")]
    public void Fills_an_empty_value(string before, string path, string after) =>
        Assert.Equal(after, Edits.Set(before, path, "Clock"));

    [Fact]
    public void Setting_the_value_it_already_has_changes_nothing()
    {
        var document = LayoutDocument.Open("a: 1   # one\n");

        Assert.Same(document, document.SetScalar(YamlPath.Parse("a"), "1"));
    }

    [Fact]
    public void Replaces_a_scalar_with_a_flow_mapping_and_keeps_the_comment() =>
        Assert.Equal(
            "style:\n  fill: { linear: [\"#000\", \"#FFF\"] }   # white\n  radius: 4\n",
            Edits.Replace("style:\n  fill: \"#FFF\"   # white\n  radius: 4\n", "style.fill", "{ linear: [\"#000\", \"#FFF\"] }"));

    [Fact]
    public void Replaces_a_flow_value_with_another() =>
        Assert.Equal(
            "- { type: text, style: { size: 2, weight: bold } }\n",
            Edits.Replace("- { type: text, style: { size: 1 } }\n", "[0].style", "{ size: 2, weight: bold }"));

    [Fact]
    public void Replaces_a_block_mapping_with_a_block_fragment_at_its_own_indentation() =>
        Assert.Equal(
            "a:\n  style:\n      size: 30\n      color: red   # last\n  next: 1\n",
            Edits.Replace("a:\n  style:\n      size: 40\n      # bold reads better\n      weight: bold   # last\n  next: 1\n",
                "a.style", "size: 30\ncolor: red\n"));

    [Fact]
    public void Replaces_a_block_mapping_with_a_flow_one_and_keeps_the_key_line_comment() =>
        Assert.Equal(
            "style: { size: 30 }  # the look\nnext: 1\n",
            Edits.Replace("style:  # the look\n  size: 40\n  weight: bold\nnext: 1\n", "style", "{ size: 30 }"));

    [Fact]
    public void Replaces_a_scalar_with_a_block_mapping_under_its_key() =>
        Assert.Equal(
            "a:\n  fill:  # white\n    linear: [\"#000\", \"#FFF\"]\n    angle: 90\n  next: 1\n",
            Edits.Replace("a:\n  fill: \"#FFF\"  # white\n  next: 1\n", "a.fill", "linear: [\"#000\", \"#FFF\"]\nangle: 90"));

    [Fact]
    public void Replaces_a_list_item_with_a_block_mapping_in_compact_form() =>
        Assert.Equal(
            "when:\n  - above: 90\n    style: hot\n  - { stale: true }\n",
            Edits.Replace("when:\n  - { above: 85, style: hot }\n  - { stale: true }\n", "when[0]", "above: 90\nstyle: hot"));

    [Fact]
    public void Replaces_a_block_list_with_a_flow_list() =>
        Assert.Equal(
            "points: [3, 4]\nx: 1\n",
            Edits.Replace("points:\n  - 1\n  - 2\nx: 1\n", "points", "[3, 4]"));

    [Fact]
    public void Replaces_a_value_with_a_multi_line_block_scalar() =>
        Assert.Equal(
            "a:\n  text: |\n    one\n    two\n  b: 1\n",
            Edits.Replace("a:\n  text: x\n  b: 1\n", "a.text", "|\n  one\n  two\n"));

    [Theory]
    [InlineData("a: { b: 1 }\n", "a.b", "c: 1\nd: 2")]
    [InlineData("a: [1, 2]\n", "a[0]", "- 1\n- 2")]
    public void A_value_inside_braces_or_brackets_must_be_one_line(string yaml, string path, string fragment)
    {
        var ex = Edits.Refused(yaml, d => d.ReplaceValue(YamlPath.Parse(path), fragment));

        Assert.Contains("one line", ex.Reason);
    }

    [Theory]
    [InlineData("{ b: ")]
    [InlineData("")]
    [InlineData("a: 1\n---\nb: 2")]
    [InlineData("&x { b: 1 }")]
    [InlineData("*x")]
    public void Refuses_a_fragment_that_is_not_one_plain_value(string fragment) =>
        Edits.Refused("a: 1\n", d => d.ReplaceValue(YamlPath.Parse("a"), fragment));

    [Fact]
    public void Refuses_to_set_a_mapping_or_list_as_a_single_value()
    {
        var ex = Edits.Refused("a: { b: 1 }\n", d => d.SetScalar(YamlPath.Parse("a"), "x"));

        Assert.Contains("a", ex.Reason);
    }

    [Theory]
    [InlineData("b")]
    [InlineData("a.b")]
    [InlineData("a[0]")]
    public void Refuses_a_path_that_is_not_in_the_file(string path)
    {
        var ex = Edits.Refused("a: 1\n", d => d.SetScalar(YamlPath.Parse(path), "x"));

        Assert.Contains(path, ex.Reason);
    }

    [Fact]
    public void Leaves_the_old_document_as_it_was()
    {
        var before = LayoutDocument.Open("a: 1\n");

        var after = before.SetScalar(YamlPath.Parse("a"), "2");

        Assert.Equal("a: 1\n", before.Text);
        Assert.Equal("a: 2\n", after.Text);
    }
}
