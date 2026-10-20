using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// Reading a whole value back from the file, as the editor does for a gradient paint or a style's own keys: without its
/// quoting, layout or comments, whatever style it is written in.
public class LayoutDocumentValueAtTests
{
    const string Yaml = """
        canvas: { orientation: landscape }   # the screen's way round
        widgets:
          - type: text
            at: [10, 20]
            text: "CPU {cpu.load:0}%"
            style:
              fill: '#FF0000'
          - { type: value, source: cpu.temp, when: [{ above: 80, style: { color: red } }] }
        """;

    static LayoutDocument Document() => LayoutDocument.Open(Yaml);

    [Fact]
    public void A_single_value_is_its_text_without_quotes()
    {
        Assert.Equal(new YamlText("CPU {cpu.load:0}%"), Document().ValueAt(YamlPath.Parse("widgets[0].text")));
        Assert.Equal(new YamlText("#FF0000"), Document().ValueAt(YamlPath.Parse("widgets[0].style.fill")));
        Assert.Equal(new YamlText("landscape"), Document().ValueAt(YamlPath.Parse("canvas.orientation")));
    }

    [Fact]
    public void A_list_and_a_mapping_are_their_values_in_order()
    {
        var at = Assert.IsType<YamlList>(Document().ValueAt(YamlPath.Parse("widgets[0].at")));
        Assert.Equal(["10", "20"], at.Items.Select(item => ((YamlText)item).Value));

        var widget = Assert.IsType<YamlMap>(Document().ValueAt(YamlPath.Parse("widgets[1]")));
        Assert.Equal(["type", "source", "when"], widget.Entries.Select(entry => entry.Key));
        var rule = Assert.IsType<YamlMap>(Assert.IsType<YamlList>(widget["when"]).Items[0]);
        Assert.Equal(new YamlText("red"), Assert.IsType<YamlMap>(rule["style"])["color"]);
    }

    [Fact]
    public void A_path_the_file_has_not_is_null()
    {
        Assert.Null(Document().ValueAt(YamlPath.Parse("widgets[2]")));
        Assert.Null(Document().ValueAt(YamlPath.Parse("widgets[0].size")));
        Assert.Null(Document().ValueAt(YamlPath.Parse("canvas.orientation.x")));
        Assert.Null(LayoutDocument.Open("").ValueAt(YamlPath.Parse("widgets")));
    }

    [Fact]
    public void The_whole_file_is_the_root()
    {
        var root = Assert.IsType<YamlMap>(Document().ValueAt(YamlPath.Root));

        Assert.Equal(["canvas", "widgets"], root.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void A_value_reached_through_an_alias_reads_as_the_value_it_names()
    {
        var document = LayoutDocument.Open("styles:\n  base: &base { size: 30 }\nwidgets:\n  - { type: text, style: *base }\n");

        Assert.Equal(new YamlText("30"), document.ValueAt(YamlPath.Parse("widgets[0].style.size")));
    }

    [Fact]
    public void A_value_that_repeats_an_anchor_inside_itself_is_refused()
    {
        var document = LayoutDocument.Open("widgets:\n  - &a { type: text }\n  - *a\n");

        Assert.Throws<LayoutEditException>(() => document.ValueAt(YamlPath.Parse("widgets")));
        Assert.Equal(new YamlText("text"), document.ValueAt(YamlPath.Parse("widgets[1].type")));
    }
}
