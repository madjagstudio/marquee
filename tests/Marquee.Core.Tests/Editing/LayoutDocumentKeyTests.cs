using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// AddKey and RemoveKey. Each case runs on LF and CRLF text (Edits).
public class LayoutDocumentKeyTests
{
    [Theory]
    [InlineData("- { type: bar, at: [1, 2] }\n", "- { type: bar, at: [1, 2], name: Bar }\n")]
    [InlineData("- { type: bar, at: [1, 2], }\n", "- { type: bar, at: [1, 2], name: Bar, }\n")]
    [InlineData("- {}\n", "- { name: Bar }\n")]
    [InlineData("- { }\n", "- { name: Bar }\n")]
    [InlineData("- { type: bar, hidden: }\n", "- { type: bar, hidden: , name: Bar }\n")]
    [InlineData("- {type: bar,at: [1, 2]}\n", "- {type: bar,at: [1, 2],name: Bar}\n")]
    [InlineData("- {\n    type: bar,\n    at: [1, 2]\n  }\n", "- {\n    type: bar,\n    at: [1, 2],\n    name: Bar\n  }\n")]
    [InlineData("- {\n    type: bar   # a meter\n  }\n", "- {\n    type: bar, name: Bar   # a meter\n  }\n")]
    public void Adds_a_key_inside_the_braces_of_a_flow_mapping(string before, string after) =>
        Assert.Equal(after, Edits.Add(before, "[0]", "name", "Bar"));

    [Fact]
    public void Adds_a_key_to_nested_flow() =>
        Assert.Equal(
            "style: { fill: { linear: [a, b], angle: 90 } }\n",
            Edits.Add("style: { fill: { linear: [a, b] } }\n", "style.fill", "angle", "90"));

    [Fact]
    public void Adds_a_key_at_the_end_of_a_block_mapping_before_the_comments_after_it() =>
        Assert.Equal(
            "canvas:\n  orientation: landscape\n  fps: 30   # smooth\n  name: Desk\n# styles next\n\nstyles: {}\n",
            Edits.Add("canvas:\n  orientation: landscape\n  fps: 30   # smooth\n# styles next\n\nstyles: {}\n", "canvas", "name", "Desk"));

    [Fact]
    public void Adds_a_key_to_a_widget_written_as_a_block_list_item() =>
        Assert.Equal(
            "widgets:\n  - type: text\n    at: [0, 0]\n    name: Title\n  - { type: bar }\n",
            Edits.Add("widgets:\n  - type: text\n    at: [0, 0]\n  - { type: bar }\n", "widgets[0]", "name", "Title"));

    [Fact]
    public void Adds_a_block_value_one_indentation_step_in()
    {
        const string before = "styles:\n    panel: { radius: 4 }\nwidgets:\n    - type: text\n      at: [0, 0]\n";

        Assert.Equal(
            before + "      style:\n          size: 40\n          weight: bold\n",
            Edits.Add(before, "widgets[0]", "style", "size: 40\nweight: bold\n"));
    }

    [Fact]
    public void Adds_a_block_scalar_value() =>
        Assert.Equal(
            "w:\n  type: text\n  text: |\n    one\n    two\n",
            Edits.Add("w:\n  type: text\n", "w", "text", "|\n  one\n  two\n"));

    [Theory]
    [InlineData("style:\nnext: 1\n", "style:\n  size: 40\nnext: 1\n")]
    [InlineData("style:   # empty for now\n", "style:   # empty for now\n  size: 40\n")]
    [InlineData("a: { style: }\n", "a: { style: { size: 40 } }\n")]
    [InlineData("a: { style: , b: 1 }\n", "a: { style: { size: 40 }, b: 1 }\n")]
    public void Adds_a_key_to_an_empty_value(string before, string after) =>
        Assert.Equal(after, Edits.Add(before, before.StartsWith("a:") ? "a.style" : "style", "size", "40"));

    [Theory]
    [InlineData("", "canvas: { fps: 30 }\r\n")]
    [InlineData("# a new theme\n", "# a new theme\ncanvas: { fps: 30 }\n")]
    [InlineData("styles: {}\n", "styles: {}\ncanvas: { fps: 30 }\n")]
    [InlineData("styles: {}", "styles: {}\r\ncanvas: { fps: 30 }\r\n")]
    public void Adds_a_top_level_key_at_the_end_of_the_file(string before, string after)
    {
        // A file with no line break yet gets Windows' CRLF, as ConfigEditor writes.
        var edited = LayoutDocument.Open(before).AddKey(YamlPath.Root, "canvas", "{ fps: 30 }").Text;

        Assert.Equal(after, edited);
    }

    [Fact]
    public void Adds_a_key_under_an_empty_value_on_the_last_line() =>
        Assert.Equal(
            "style:\r\n  size: 40\r\n",
            LayoutDocument.Open("style:").AddKey(YamlPath.Parse("style"), "size", "40").Text);

    [Fact]
    public void Quotes_a_key_only_when_YAML_would_read_it_differently()
    {
        Assert.Equal(
            "styles:\n  panel: {}\n  my style: { size: 4 }\n",
            Edits.Add("styles:\n  panel: {}\n", "styles", "my style", "{ size: 4 }"));
        Assert.Equal(
            "styles:\n  panel: {}\n  '#hot': { size: 4 }\n",
            Edits.Add("styles:\n  panel: {}\n", "styles", "#hot", "{ size: 4 }"));
        Assert.Equal(
            "styles: { panel: {}, 'a, b': { size: 4 } }\n",
            Edits.Add("styles: { panel: {} }\n", "styles", "a, b", "{ size: 4 }"));
    }

    [Fact]
    public void Refuses_a_key_the_mapping_already_has()
    {
        var ex = Edits.Refused("a: { b: 1 }\n", d => d.AddKey(YamlPath.Parse("a"), "b", "2"));

        Assert.Contains("already", ex.Reason);
    }

    [Fact]
    public void Refuses_a_multi_line_value_inside_braces() =>
        Edits.Refused("a: { b: 1 }\n", d => d.AddKey(YamlPath.Parse("a"), "c", "x: 1\ny: 2"));

    [Theory]
    [InlineData("a: 1\n")]
    [InlineData("a: [1]\n")]
    public void Refuses_to_add_a_key_to_something_that_is_not_a_mapping(string yaml) =>
        Edits.Refused(yaml, d => d.AddKey(YamlPath.Parse("a"), "b", "2"));

    [Theory]
    [InlineData("type", "- { at: [1, 2], style: meter }\n")]
    [InlineData("at", "- { type: bar, style: meter }\n")]
    [InlineData("style", "- { type: bar, at: [1, 2] }\n")]
    public void Removes_a_key_from_a_one_line_flow_mapping(string key, string after) =>
        Assert.Equal(after, Edits.Remove("- { type: bar, at: [1, 2], style: meter }\n", $"[0].{key}"));

    [Theory]
    [InlineData("style: { size: 1 }\n", "style: { }\n")]
    [InlineData("style: {size: 1}\n", "style: { }\n")]
    [InlineData("style: { size: 1, }\n", "style: { }\n")]
    [InlineData("style: {\n  size: 1   # big\n}\n", "style: { }\n")]
    public void Removing_the_last_key_of_a_flow_mapping_leaves_empty_braces(string before, string after) =>
        Assert.Equal(after, Edits.Remove(before, "style.size"));

    [Fact]
    public void Removes_a_key_from_nested_flow() =>
        Assert.Equal(
            "style: { fill: { linear: [a, b] } }\n",
            Edits.Remove("style: { fill: { linear: [a, b], angle: 90 } }\n", "style.fill.angle"));

    [Fact]
    public void Removes_a_key_from_multi_line_flow_with_its_comment() =>
        Assert.Equal(
            "a: {\n  c: 2\n}\n",
            Edits.Remove("a: {\n  b: 1,   # one\n  c: 2\n}\n", "a.b"));

    const string Styles =
        "styles:\n" +
        "  hot:\n" +
        "    color: red\n" +
        "    # A soft glow.\n" +
        "    glow: { color: red, radius: 18 }\n" +
        "    pulse:   # breathe\n" +
        "      property: glow\n" +
        "      # slower is calmer\n" +
        "      period: 1200\n" +
        "      # min: 0.2\n" +
        "    # about hot\n" +
        "  stale: { color: grey }\n";

    [Fact]
    public void Removes_a_block_key_with_the_comment_lines_directly_above_it() =>
        Assert.Equal(
            Styles.Replace("    # A soft glow.\n    glow: { color: red, radius: 18 }\n", ""),
            Edits.Remove(Styles, "styles.hot.glow"));

    [Fact]
    public void Removes_a_block_key_with_its_whole_value_and_the_comments_inside_it() =>
        Assert.Equal(
            Styles.Replace("    pulse:   # breathe\n      property: glow\n      # slower is calmer\n      period: 1200\n      # min: 0.2\n", ""),
            Edits.Remove(Styles, "styles.hot.pulse"));

    [Fact]
    public void Removes_a_block_key_whose_value_holds_more_blocks() =>
        Assert.Equal("styles:\n  stale: { color: grey }\n", Edits.Remove(Styles, "styles.hot"));

    [Theory]
    [InlineData("a: 1\n\nb: 2\n\nc: 3\n", "b", "a: 1\n\nc: 3\n")]
    [InlineData("a: 1\n\nb: 2\nc: 3\n", "b", "a: 1\n\nc: 3\n")]
    [InlineData("a: 1\nb: 2\n\nc: 3\n", "b", "a: 1\n\nc: 3\n")]
    [InlineData("a: 1\n\n# about b\nb: 2\n\nc: 3\n", "b", "a: 1\n\nc: 3\n")]
    [InlineData("a: 1\nb: 2", "b", "a: 1\n")]
    [InlineData("t: |\n  abc\n\nnext: 1\n", "t", "next: 1\n")]
    public void Removing_a_block_key_leaves_one_blank_line_between_neighbours(string before, string key, string after) =>
        Assert.Equal(after, Edits.Remove(before, key));

    [Fact]
    public void Keeps_the_comments_that_start_the_file() =>
        Assert.Equal(
            "# My theme\n# for the desk\nstyles: {}\n",
            Edits.Remove("# My theme\n# for the desk\ncanvas:\n  fps: 30\nstyles: {}\n", "canvas"));

    [Theory]
    [InlineData("canvas:\n  fps: 30\nx: 1\n", "canvas.fps", "canvas: { }\nx: 1\n")]
    [InlineData("canvas:   # the screen\n  fps: 30\n", "canvas.fps", "canvas: { }   # the screen\n")]
    [InlineData("- type: text\n- b\n", "[0].type", "- { }\n- b\n")]
    [InlineData("# theme\na: 1\n", "a", "# theme\n")]
    [InlineData("a: 1\n", "a", "")]
    public void Removing_the_last_key_of_a_block_mapping_leaves_an_empty_mapping(string before, string path, string after) =>
        Assert.Equal(after, Edits.Remove(before, path));

    [Fact]
    public void Removes_the_first_key_of_a_widget_that_starts_on_the_dash_line() =>
        Assert.Equal(
            "widgets:\n  - at: [0, 0]\n    text: CPU\n",
            Edits.Remove("widgets:\n  - type: text   # a label\n    at: [0, 0]\n    text: CPU\n", "widgets[0].type"));

    [Theory]
    [InlineData("a: 1\n", "b")]
    [InlineData("a: [1, 2]\n", "a[0]")]
    [InlineData("a: 1\n", "")]
    public void Refuses_to_remove_what_is_not_a_key_in_the_file(string yaml, string path) =>
        Edits.Refused(yaml, d => d.RemoveKey(YamlPath.Parse(path)));
}
