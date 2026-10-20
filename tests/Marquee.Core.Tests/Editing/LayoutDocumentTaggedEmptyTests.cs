using Marquee.Core.Config;

namespace Marquee.Core.Tests.Editing;

/// An empty value with a tag or anchor in front of it (`a: !t`), which YamlDotNet ends at the next token, past any
/// blank and comment lines: it ends after its tag, so edits of it and beside it keep their neighbours' comments. Each
/// case runs on LF and CRLF text (Edits).
public class LayoutDocumentTaggedEmptyTests
{
    [Fact]
    public void Removing_it_keeps_the_comment_above_the_next_key() =>
        Assert.Equal("m:\n  # about b\n  b: 1\n", Edits.Remove("m:\n  a: !t\n  # about b\n  b: 1\n", "m.a"));

    [Fact]
    public void Removing_the_key_before_it_keeps_it() =>
        Assert.Equal("m:\n  a: !t\n  # about b\n  b: 1\n", Edits.Remove("m:\n  z: 0\n  a: !t\n  # about b\n  b: 1\n", "m.z"));

    [Theory]
    [InlineData("m:\n  a: !t\n  # about b\n  b: 1\n", "m:\n  a: !t x\n  # about b\n  b: 1\n")]
    [InlineData("m:\n  a: !t\n\n  b: 1\n", "m:\n  a: !t x\n\n  b: 1\n")]
    [InlineData("m: { a: !t , b: 1 }\n", "m: { a: !t x , b: 1 }\n")]
    [InlineData("m: { b: 1, a: !t }\n", "m: { b: 1, a: !t x }\n")]
    [InlineData("m:\n  a: !t   # c\n  b: 1\n", "m:\n  a: !t x   # c\n  b: 1\n")]
    public void Setting_it_writes_the_value_after_the_tag(string before, string after) =>
        Assert.Equal(after, Edits.Set(before, "m.a", "x"));

    [Fact]
    public void Adding_a_key_after_it_goes_after_its_tag() =>
        Assert.Equal("m:\n  a: !t\n  k: 1\n# end\n", Edits.Add("m:\n  a: !t\n# end\n", "m", "k", "1"));

    [Fact]
    public void ConfigEditor_sets_a_tagged_empty_value_and_keeps_its_tag() =>
        Assert.Equal("theme: !!str new\nx: 1\n", ConfigEditor.Set("theme: !!str\nx: 1\n", "theme", "new"));
}
