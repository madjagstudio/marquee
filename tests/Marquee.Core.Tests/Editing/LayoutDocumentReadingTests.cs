using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// Reading the file through LayoutDocument: single values, and what a node is.
public class LayoutDocumentReadingTests
{
    const string Yaml =
        "canvas:   # the screen\n" +                         // 1
        "  orientation: landscape\n" +                       // 2
        "  fps: 30\n" +                                      // 3
        "styles:\n" +                                        // 4
        "  label: { size: 26, color: \"#8B95AA\" }\n" +      // 5
        "  empty:\n" +                                       // 6
        "  tagged: !!str 12\n" +                             // 7
        "widgets:\n" +                                       // 8
        "  # The title.\n" +                                 // 9
        "  - type: text\n" +                                 // 10
        "    at: [0, 24]\n" +                                // 11
        "    text: |\n" +                                    // 12
        "      CPU\n" +                                      // 13
        "      load\n" +                                     // 14
        "  - { type: group, children: [] }\n";               // 15

    static LayoutDocument Document => LayoutDocument.Open(Yaml);

    [Theory]
    [InlineData("canvas.orientation", "landscape")]
    [InlineData("canvas.fps", "30")]
    [InlineData("styles.label.color", "#8B95AA")]
    [InlineData("styles.empty", "")]
    [InlineData("styles.tagged", "12")]
    [InlineData("widgets[0].at[1]", "24")]
    [InlineData("widgets[0].text", "CPU\nload\n")]
    public void Reads_a_single_value_as_LayoutParser_reads_it(string path, string expected)
    {
        Assert.True(Document.TryGetScalar(YamlPath.Parse(path), out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("canvas")]
    [InlineData("widgets[0].at")]
    [InlineData("widgets[9]")]
    [InlineData("nothing")]
    [InlineData("")]
    public void A_list_mapping_or_missing_node_is_not_a_single_value(string path)
    {
        Assert.False(Document.TryGetScalar(YamlPath.Parse(path), out var value));
        Assert.Null(value);
    }

    [Fact]
    public void An_empty_file_has_no_nodes()
    {
        var empty = LayoutDocument.Open("# nothing yet\n");

        Assert.Null(empty.GetNode(YamlPath.Root));
        Assert.False(empty.TryGetScalar(YamlPath.Root, out _));
    }

    [Fact]
    public void Describes_a_block_mapping()
    {
        var node = Document.GetNode(YamlPath.Parse("canvas"))!;

        Assert.Equal(LayoutNodeKind.Mapping, node.Kind);
        Assert.False(node.IsFlow);
        Assert.Equal(["orientation", "fps"], node.Keys);
        Assert.Equal(2, node.Count);
        Assert.Equal(1, node.Line);
        Assert.True(node.IsEditable);
        Assert.Equal(YamlPath.Parse("canvas"), node.Path);
    }

    [Fact]
    public void Describes_a_flow_mapping_on_its_keys_line()
    {
        var node = Document.GetNode(YamlPath.Parse("styles.label"))!;

        Assert.Equal((LayoutNodeKind.Mapping, true, 2, 5), (node.Kind, node.IsFlow, node.Count, node.Line));
        Assert.Equal(["size", "color"], node.Keys);
    }

    [Theory]
    [InlineData("widgets", LayoutNodeKind.List, false, 2, 8)]
    [InlineData("widgets[0]", LayoutNodeKind.Mapping, false, 3, 10)]
    [InlineData("widgets[0].at", LayoutNodeKind.List, true, 2, 11)]
    [InlineData("widgets[1].children", LayoutNodeKind.List, true, 0, 15)]
    [InlineData("widgets[0].text", LayoutNodeKind.Scalar, false, 0, 12)]
    [InlineData("styles.empty", LayoutNodeKind.Empty, false, 0, 6)]
    [InlineData("styles.tagged", LayoutNodeKind.Scalar, false, 0, 7)]
    [InlineData("", LayoutNodeKind.Mapping, false, 3, 1)]
    public void Describes_each_kind_of_node(string path, LayoutNodeKind kind, bool flow, int count, int line)
    {
        var node = Document.GetNode(YamlPath.Parse(path))!;

        Assert.Equal((kind, flow, count, line), (node.Kind, node.IsFlow, node.Count, node.Line));
        if (kind != LayoutNodeKind.Mapping) Assert.Empty(node.Keys);
    }

    [Fact]
    public void A_missing_node_is_null() => Assert.Null(Document.GetNode(YamlPath.Parse("widgets[0].style")));

    [Fact]
    public void A_mapping_lists_only_the_keys_a_path_can_name()
    {
        var node = LayoutDocument.Open("a: 1\n? [x, y]\n: 2\nb: 3\n").GetNode(YamlPath.Root)!;

        Assert.Equal(["a", "b"], node.Keys);
        Assert.Equal(3, node.Count);
    }

    [Fact]
    public void A_node_with_an_anchor_in_it_is_not_editable()
    {
        var document = LayoutDocument.Open("s: &s { size: 1 }\nw:\n  - { type: text, style: *s, text: A }\n");

        Assert.False(document.GetNode(YamlPath.Parse("w[0]"))!.IsEditable);
        Assert.True(document.GetNode(YamlPath.Parse("w[0].text"))!.IsEditable);
        Assert.True(document.TryGetScalar(YamlPath.Parse("w[0].style.size"), out var size));
        Assert.Equal("1", size);
    }
}
