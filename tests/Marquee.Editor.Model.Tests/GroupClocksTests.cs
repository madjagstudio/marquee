using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model.Tests;

public class GroupClocksTests
{
    const string Layout =
        "widgets:\n" +
        "  - type: group\n" +
        "    size: [320, 200]\n" +
        "    children:\n" +
        "      - { type: text, at: [0, 0], size: [320, 85], text: \"{now|time:HH:mm}\" }\n" +
        "      - { type: text, at: [0, 90], size: [320, 40], text: \"{now|time:ddd, d MMM}\" }\n" +
        "      - { type: text, at: [0, 140], size: [320, 40], text: \"CPU {cpu.temp:0}\" }\n" +
        "      - type: group\n" +
        "        size: [100, 40]\n" +
        "        children:\n" +
        "          - { type: text, at: [0, 0], size: [100, 40], text: \"{now|time:HH:mm:ss}\" }\n" +
        "  - { type: text, at: [400, 0], size: [100, 40], text: \"{now|time:HH:mm}\" }\n";

    static YamlPath P(string path) => YamlPath.Parse(path);

    [Fact]
    public void A_groups_clocks_are_its_time_and_date_texts_at_any_depth()
    {
        var document = LayoutDocument.Open(Layout);
        Assert.Equal([P("widgets[0].children[0]"), P("widgets[0].children[1]"), P("widgets[0].children[3].children[0]")],
            GroupClocks.In(document, P("widgets[0]")));
        Assert.Empty(GroupClocks.In(document, P("widgets[1]")));
    }

    [Fact]
    public void The_shared_format_is_null_when_the_clocks_differ_and_a_change_rewrites_only_that_part()
    {
        var document = LayoutDocument.Open(Layout);
        var clocks = GroupClocks.In(document, P("widgets[0]"));
        Assert.Null(GroupClocks.Format(document, clocks, TokenKind.Time));
        Assert.Equal("ddd, d MMM", GroupClocks.Format(document, clocks, TokenKind.Date));

        var changed = GroupClocks.Change(document, clocks, TokenKind.Time, TextTemplate.Pattern(TimeFormat.Hour12));
        Assert.Equal("{now|time:h:mm tt}", Text(changed, "widgets[0].children[0]"));
        Assert.Equal("{now|time:ddd, d MMM}", Text(changed, "widgets[0].children[1]"));
        Assert.Equal("{now|time:h:mm tt}", Text(changed, "widgets[0].children[3].children[0]"));
        Assert.Equal("CPU {cpu.temp:0}", Text(changed, "widgets[0].children[2]"));
        Assert.Equal("{now|time:HH:mm}", Text(changed, "widgets[1]"));
        Assert.Equal("h:mm tt", GroupClocks.Format(changed, clocks, TokenKind.Time));
    }

    static string Text(LayoutDocument document, string widget) =>
        document.TryGetScalar(P(widget).Child("text"), out var text) ? text : "";
}
