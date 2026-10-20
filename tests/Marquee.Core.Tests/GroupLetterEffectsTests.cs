using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A group passes its letter effects down to its children, as it passes its font, size, color and alignment: a child
/// drawn inside the group looks exactly like the same widget drawn alone with the group's keys in its own style, and a
/// key the child sets itself wins.
public class GroupLetterEffectsTests
{
    const string Landscape = "canvas: { orientation: landscape }\nwidgets:\n";

    static SKImage Render(string widgets) => Frames.Render(Landscape + widgets, TestSensors.Of());

    static void AssertSamePixels(SKImage expected, SKImage actual)
    {
        using var a = SKBitmap.FromImage(expected);
        using var b = SKBitmap.FromImage(actual);
        Assert.True(a.Pixels.SequenceEqual(b.Pixels), "The two frames differ");
    }

    /// One text widget at [40, 40] inside a group, with the group's style and the child's. The group is 20 px larger
    /// all round, as it clips its children, and an outlined underline reaches past the text's box.
    static string Grouped(string groupStyle, string text, string childStyle = "{}") => $$"""
          - type: group
            at: [20, 20]
            size: [840, 240]
            style: {{groupStyle}}
            children:
              - { type: text, at: [20, 20], size: [800, 200], text: "{{text}}", style: {{childStyle}} }
        """;

    static string Alone(string text, string style) => $$"""
          - { type: text, at: [40, 40], size: [800, 200], text: "{{text}}", style: {{style}} }
        """;

    [Fact]
    public void A_groups_case_and_italic_reach_its_child_text()
    {
        using var grouped = Render(Grouped("{ size: 64, case: upper, italic: true }", "Cpu hot"));
        using var alone = Render(Alone("CPU HOT", "{ size: 64, italic: true }"));

        AssertSamePixels(alone, grouped);
    }

    [Fact]
    public void A_childs_own_case_and_italic_win_over_its_groups()
    {
        using var grouped = Render(Grouped("{ size: 64, case: upper, italic: true }", "Cpu Hot", "{ case: lower, italic: false }"));
        using var alone = Render(Alone("cpu hot", "{ size: 64 }"));

        AssertSamePixels(alone, grouped);
    }

    [Fact]
    public void A_groups_lines_outline_and_fill_reach_its_child_and_the_outline_merges_key_by_key()
    {
        const string Group = """{ size: 64, underline: true, strike: true, text_fill: { linear: ["#FF0000", "#0000FF"] }, text_stroke: { width: 3, color: "#00FF00" } }""";
        using var grouped = Render(Grouped(Group, "Cpu", """{ text_stroke: { color: "#FFFFFF" } }"""));
        using var alone = Render(Alone("Cpu",
            """{ size: 64, underline: true, strike: true, text_fill: { linear: ["#FF0000", "#0000FF"] }, text_stroke: { width: 3, color: "#FFFFFF" } }"""));

        AssertSamePixels(alone, grouped);
    }

    [Fact]
    public void Without_the_group_passing_them_its_child_would_draw_plain_text()
    {
        // Guards the tests above: the group's keys do change the child, so their equal frames mean something.
        using var grouped = Render(Grouped("{ size: 64, case: upper, italic: true }", "Cpu hot"));
        using var plain = Render(Alone("Cpu hot", "{ size: 64 }"));

        using var a = SKBitmap.FromImage(plain);
        using var b = SKBitmap.FromImage(grouped);
        Assert.False(a.Pixels.SequenceEqual(b.Pixels), "The group's case and italic changed nothing");
    }
}
