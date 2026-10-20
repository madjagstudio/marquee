using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// Text with half of a UTF-16 surrogate pair can't be written as UTF-8 (the encoder writes U+FFFD in its place), so the
/// saved file wouldn't be the document's text: LayoutDocument refuses to take it in, from a file or an edit.
public class LayoutDocumentSurrogateTests
{
    public static TheoryData<string> Halves => new() { "\uD800", "a\uDC00b", "x\uD83D" };

    [Theory]
    [MemberData(nameof(Halves))]
    public void Setting_a_value_with_half_a_pair_is_refused(string value)
    {
        var ex = Edits.Refused("a: x\n", d => d.SetScalar(YamlPath.Parse("a"), value));

        Assert.Contains("surrogate", ex.Reason);
    }

    [Theory]
    [MemberData(nameof(Halves))]
    public void A_key_or_new_value_with_half_a_pair_is_refused(string text)
    {
        Edits.Refused("a: x\n", d => d.AddKey(YamlPath.Root, text, "1"));
        Edits.Refused("a: x\n", d => d.AddKey(YamlPath.Root, "b", text));
        Edits.Refused("a: x\n", d => d.ReplaceValue(YamlPath.Parse("a"), text));
        Assert.Throws<SpliceException>(() => YamlFragment.Parse(text));
    }

    [Fact]
    public void A_file_with_half_a_pair_is_refused_at_its_line()
    {
        var ex = Assert.Throws<LayoutEditException>(() => LayoutDocument.Open("a: 1\nb: x\uDC00\n"));

        Assert.Equal(2, ex.Line);
        Assert.Contains("surrogate", ex.Reason);
    }

    [Fact]
    public void A_whole_pair_is_written() =>
        Assert.Equal("a: x😀\nb: 😀\n", Edits.Add(Edits.Set("a: x\n", "a", "x😀"), "", "b", "😀"));
}
