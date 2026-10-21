using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// A layout.yaml that starts with a UTF-8 byte order mark, read without decoding it: the mark stays in Text through
/// every edit, and the first key is still found by its name. Each case runs on LF and CRLF text (Edits).
public class LayoutDocumentBomTests
{
    const string Bom = "\uFEFF";

    const string Yaml = Bom + "canvas: { orientation: landscape }\nwidgets:\n  - { type: text, text: A }\n";

    [Fact]
    public void The_first_key_is_found_by_its_name()
    {
        var document = LayoutDocument.Open(Yaml);

        Assert.True(document.Exists(YamlPath.Parse("canvas.orientation")));
        Assert.Equal(Yaml, document.Text);
    }

    [Fact]
    public void Setting_the_first_key_keeps_the_mark() =>
        Assert.Equal(Yaml.Replace("landscape", "portrait"), Edits.Set(Yaml, "canvas.orientation", "portrait"));

    [Fact]
    public void Removing_the_first_key_keeps_the_mark() =>
        Assert.Equal(Bom + "widgets:\n  - { type: text, text: A }\n", Edits.Remove(Yaml, "canvas"));

    [Fact]
    public void Adding_a_key_keeps_the_mark() =>
        Assert.Equal(Yaml + "name: x\n", Edits.Apply(Yaml, d => d.AddKey(YamlPath.Root, "name", "x")));

    [Fact]
    public void List_edits_keep_the_mark() =>
        Assert.Equal(
            Bom + "canvas: { orientation: landscape }\nwidgets:\n  - { type: text, text: B }\n  - { type: text, text: A }\n",
            Edits.Apply(Yaml, d => d.InsertItem(YamlPath.Parse("widgets"), 0, "{ type: text, text: B }")));

    [Fact]
    public void A_mark_alone_is_an_empty_file() =>
        Assert.Equal(Bom + "name: x\r\n", LayoutDocument.Open(Bom).AddKey(YamlPath.Root, "name", "x").Text);

    [Fact]
    public void A_refusal_counts_lines_as_the_file_does()
    {
        var ex = Assert.Throws<LayoutEditException>(() => LayoutDocument.Open(Bom + "a: 1\nb: 2\na: 3\n"));

        Assert.Equal(3, ex.Line);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void A_line_is_found_as_the_file_counts_it(string eol)
    {
        var document = LayoutDocument.Open((Bom + "{ a: [\n  1,\n\n  2,\n], b: 3\n}").Replace("\n", eol));

        Assert.Equal(YamlPath.Parse("a[0]"), document.PathAt(2));
        Assert.Equal(YamlPath.Parse("a"), document.PathAt(3));
        Assert.Equal(YamlPath.Parse("a[1]"), document.PathAt(4));
        Assert.Equal(YamlPath.Root, document.PathAt(5));
        Assert.Equal(YamlPath.Root, document.PathAt(6));
        Assert.Null(document.PathAt(7));
    }

    [Fact]
    public void A_comment_moving_up_to_a_block_texts_header_keeps_the_mark() =>
        Assert.Equal(Bom + "a: |   # c\n  x\n  y\nb: 2\n", Edits.Replace(Bom + "a: 1   # c\nb: 2\n", "a", "|\n  x\n  y"));
}
