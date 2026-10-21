using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// PathAt: the node a line belongs to, so an error LayoutParser reports at a line can be shown on its widget.
public class LayoutDocumentPathAtTests
{
    const string Yaml =
        "# A theme.\n" +                                     // 1
        "canvas:   # the screen\n" +                         // 2
        "  orientation: landscape\n" +                       // 3
        "  fps: 30\n" +                                      // 4
        "styles:\n" +                                        // 5
        "  label: { size: 26, color: \"#8B95AA\" }\n" +      // 6
        "  empty:\n" +                                       // 7
        "widgets:\n" +                                       // 8
        "  # The title.\n" +                                 // 9
        "  - type: text\n" +                                 // 10
        "    at: [0, 24]\n" +                                // 11
        "    text: |\n" +                                    // 12
        "      CPU\n" +                                      // 13
        "\n" +                                               // 14
        "      load\n" +                                     // 15
        "    # trailing\n" +                                 // 16
        "  - { type: group, children: [\n" +                 // 17
        "      { type: text, text: a }\n" +                  // 18
        "    ] }\n";                                         // 19

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "canvas")]
    [InlineData(3, "canvas.orientation")]
    [InlineData(4, "canvas.fps")]
    [InlineData(5, "styles")]
    [InlineData(6, "styles.label")]
    [InlineData(7, "styles.empty")]
    [InlineData(8, "widgets")]
    [InlineData(9, "widgets")]
    [InlineData(10, "widgets[0].type")]
    [InlineData(11, "widgets[0].at")]
    [InlineData(12, "widgets[0].text")]
    [InlineData(13, "widgets[0].text")]
    [InlineData(14, "widgets[0].text")]
    [InlineData(15, "widgets[0].text")]
    [InlineData(16, "widgets")]
    [InlineData(17, "widgets[1]")]
    [InlineData(18, "widgets[1].children[0]")]
    [InlineData(19, "widgets[1].children")]
    public void Finds_the_deepest_node_on_a_line(int line, string path) =>
        Assert.Equal(YamlPath.Parse(path), LayoutDocument.Open(Yaml).PathAt(line));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(20)]
    public void A_line_the_file_has_not_has_no_node(int line) => Assert.Null(LayoutDocument.Open(Yaml).PathAt(line));

    [Fact]
    public void An_empty_file_has_no_node() => Assert.Null(LayoutDocument.Open("# nothing\n").PathAt(1));

    [Fact]
    public void Counts_lines_the_same_with_CRLF() =>
        Assert.Equal(YamlPath.Parse("widgets[0].text"), LayoutDocument.Open(Yaml.Replace("\n", "\r\n")).PathAt(14));

    [Fact]
    public void Stops_at_an_alias_whose_text_is_elsewhere()
    {
        var document = LayoutDocument.Open("s: &s\n  size: 1\nw:\n  - type: text\n    style: *s\n");

        Assert.Equal(YamlPath.Parse("s.size"), document.PathAt(2));
        Assert.Equal(YamlPath.Parse("w[0].style"), document.PathAt(5));
    }
}
