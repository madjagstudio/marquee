using System.Runtime.CompilerServices;

namespace Marquee.Core.Tests.Editing;

/// Where a comment beside or inside a value goes when the value is replaced. Each case runs on LF and CRLF text (Edits).
public class LayoutDocumentCommentTests
{
    static string Fixture(string name, [CallerFilePath] string file = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "Fixtures", name + ".yaml")).Replace("\r\n", "\n");

    [Fact]
    public void A_comment_after_a_value_replaced_by_a_block_text_moves_up_to_its_header() =>
        Assert.Equal("a: |   # c\n  x\n  y\nb: 2\n", Edits.Replace("a: 1   # c\nb: 2\n", "a", "|\n  x\n  y"));

    [Fact]
    public void A_comment_after_a_value_replaced_by_a_block_text_moves_up_in_the_fixture()
    {
        var before = Fixture("comments");

        Assert.Equal(
            before.Replace(
                "  orientation: landscape   # the way it is mounted\n",
                "  orientation: |-   # the way it is mounted\n    a\n    b\n"),
            Edits.Replace(before, "canvas.orientation", "|-\n  a\n  b"));
    }

    [Theory]
    [InlineData("w:   # list\n  - 1   # x\nz: 1\n", "5", "w: 5   # list\nz: 1\n")]
    [InlineData("w:   # list\n  a: 1\n  c: [x]   # x\n\n  # after\nz: 1\n", "{ }", "w: { }   # list\n\n  # after\nz: 1\n")]
    [InlineData("w:\n  - 1   # x\nz: 1\n", "[]", "w: []\nz: 1\n")]
    [InlineData("w:   # list\n  - 1   # x\nz: 1\n", "|\n  a\n  b", "w: |   # list\n  a\n  b\nz: 1\n")]
    public void A_block_replaced_by_a_value_beside_its_key_takes_its_last_comment_with_it(string before, string value, string after) =>
        Assert.Equal(after, Edits.Replace(before, "w", value));

    [Theory]
    [InlineData("x: |  # c\n  text\ny: 1\n", " ", "x: \" \"  # c\ny: 1\n")]
    [InlineData("x: >-   # c\n  text\n\ny: 1\n", "a\tb", "x: \"a\\u0009b\"   # c\n\ny: 1\n")]
    public void A_block_text_set_to_a_value_it_can_not_hold_keeps_its_header_comment(string before, string value, string after) =>
        Assert.Equal(after, Edits.Set(before, "x", value));
}
