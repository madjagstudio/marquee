using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

public class YamlWriterTests
{
    static readonly YamlMap Rule = new([new("above", "90"), new("style", "hot")]);

    [Theory]
    [InlineData("text", false, "text")]
    [InlineData("GPU LOAD", false, "GPU LOAD")]
    [InlineData("12.5", false, "12.5")]
    [InlineData("#FF5A36", false, "\"#FF5A36\"")]
    [InlineData("{0:0}°C", false, "\"{0:0}°C\"")]
    [InlineData("a, b", false, "a, b")]
    [InlineData("a, b", true, "\"a, b\"")]
    [InlineData("cpu: hot", false, "\"cpu: hot\"")]
    [InlineData("", false, "\"\"")]
    [InlineData("line\nbreak", false, "\"line\\u000Abreak\"")]
    [InlineData(@"C:\x", false, @"C:\x")]
    [InlineData(@"C:\x", true, @"""C:\\x""")]
    public void Writes_a_value_plain_where_it_reads_back_the_same_and_double_quoted_otherwise(string value, bool inFlow, string yaml) =>
        Assert.Equal(yaml, YamlWriter.Scalar(value, inFlow));

    [Fact]
    public void Writes_a_flow_mapping_on_one_line()
    {
        var widget = new YamlMap([
            new("type", "text"),
            new("at", new YamlList(["0", "0"])),
            new("text", "CPU {cpu.load:0}%"),
            new("style", new YamlMap([new("size", YamlValue.Number(30))])),
        ]);

        Assert.Equal("{ type: text, at: [0, 0], text: \"CPU {cpu.load:0}%\", style: { size: 30 } }", YamlWriter.Flow(widget));
    }

    [Fact]
    public void Writes_empty_collections() =>
        Assert.Equal("{ a: [], b: { } }", YamlWriter.Flow(new YamlMap([new("a", new YamlList([])), new("b", YamlMap.Empty)])));

    [Fact]
    public void Writes_a_block_mapping_with_lists_of_mappings_as_block_lists_of_one_liners() =>
        Assert.Equal(
            "type: bar\nat: [0, 0]\nwhen:\n  - { above: 90, style: hot }\n  - { stale: true, style: { opacity: 0 } }\nstyle: { radius: 3 }",
            YamlWriter.Block(new YamlMap([
                new("type", "bar"),
                new("at", new YamlList(["0", "0"])),
                new("when", new YamlList([Rule, new YamlMap([new("stale", "true"), new("style", new YamlMap([new("opacity", "0")]))])])),
                new("style", new YamlMap([new("radius", "3")])),
            ])));

    [Fact]
    public void Writes_an_item_that_holds_a_list_of_mappings_in_block_style() =>
        Assert.Equal(
            "type: group\nchildren:\n    - { type: a }\n    - type: value\n      when:\n          - { above: 90, style: hot }\n    - - { x: 1 }",
            YamlWriter.Block(new YamlMap([
                new("type", "group"),
                new("children", new YamlList([
                    new YamlMap([new("type", "a")]),
                    new YamlMap([new("type", "value"), new("when", new YamlList([Rule]))]),
                    new YamlList([new YamlMap([new("x", "1")])]),
                ])),
            ]), step: 4));

    [Fact]
    public void Writes_a_mapping_that_holds_a_list_of_mappings_under_its_key() =>
        Assert.Equal(
            "graph:\n  series:\n    - { source: cpu.load }",
            YamlWriter.Block(new YamlMap([new("graph", new YamlMap([new("series", new YamlList([new YamlMap([new("source", "cpu.load")])]))]))])));

    [Fact]
    public void Write_chooses_one_line_unless_a_list_of_mappings_needs_block_style()
    {
        Assert.Equal("{ above: 90, style: hot }", YamlWriter.Write(Rule));
        Assert.Equal("w:\n  - { above: 90, style: hot }", YamlWriter.Write(new YamlMap([new("w", new YamlList([Rule]))])));
        Assert.Equal("[1, 2]", YamlWriter.Write(new YamlList(["1", "2"])));
        Assert.Equal("\"#FFF\"", YamlWriter.Write(new YamlText("#FFF")));
    }

    [Fact]
    public void Reads_a_value_into_the_model()
    {
        var read = YamlValue.Parse("type: text   # a title\nat: [0, 0]\nstyle: { size: 30 }\nwhen:\n  - { above: 1, style: hot }\n");

        Assert.Equal("type: text\nat: [0, 0]\nstyle: { size: 30 }\nwhen:\n  - { above: 1, style: hot }", YamlWriter.Block((YamlMap)read));
        Assert.Equal(new YamlText("text"), ((YamlMap)read)["type"]);
        Assert.Null(((YamlMap)read)["size"]);
    }

    [Fact]
    public void Changes_a_mapping_without_changing_its_order()
    {
        var map = new YamlMap([new("type", "text"), new("at", new YamlList(["0", "0"]))]);

        Assert.Equal("{ type: text, at: [20, 20] }", YamlWriter.Flow(map.With("at", new YamlList(["20", "20"]))));
        Assert.Equal("{ type: text, at: [0, 0], name: T }", YamlWriter.Flow(map.With("name", "T")));
        Assert.Equal("{ at: [0, 0] }", YamlWriter.Flow(map.Without("type")));
    }

    [Theory]
    [InlineData(30, "30")]
    [InlineData(-12.5, "-12.5")]
    [InlineData(0.1, "0.1")]
    public void Writes_numbers_in_the_invariant_culture(double number, string text) =>
        Assert.Equal(text, YamlValue.Number(number).Value);

    [Theory]
    [InlineData("&a 1", "anchor")]
    [InlineData("? [a]\n: 1", "key")]
    [InlineData("a: 1\n---\nb: 2", "one YAML value")]
    public void Refuses_text_it_cant_model(string yaml, string reason) =>
        Assert.Contains(reason, Assert.Throws<LayoutEditException>(() => YamlValue.Parse(yaml)).Reason);
}
