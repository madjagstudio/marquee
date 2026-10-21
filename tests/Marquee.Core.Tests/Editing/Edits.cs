using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// Runs a LayoutDocument edit on a text written with LF line breaks, and again on the same text with CRLF: the CRLF
/// result must be the LF one with CRLF line breaks, so every test also checks that an edit keeps the file's line
/// endings. Returns the LF result.
static class Edits
{
    public static string Apply(string yaml, Func<LayoutDocument, LayoutDocument> edit)
    {
        Assert.DoesNotContain("\r", yaml);
        var before = LayoutDocument.Open(yaml);
        var after = edit(before).Text;
        Assert.Equal(yaml, before.Text);

        var crlf = edit(LayoutDocument.Open(Crlf(yaml))).Text;
        Assert.Equal(Crlf(after), crlf);
        return after;
    }

    /// The edit is refused on both line endings, and the reason is returned.
    public static LayoutEditException Refused(string yaml, Func<LayoutDocument, LayoutDocument> edit)
    {
        var document = LayoutDocument.Open(yaml);
        var ex = Assert.Throws<LayoutEditException>(() => edit(document));
        Assert.Equal(yaml, document.Text);
        Assert.Throws<LayoutEditException>(() => edit(LayoutDocument.Open(Crlf(yaml))));
        return ex;
    }

    public static string Set(string yaml, string path, string value) =>
        Apply(yaml, d => d.SetScalar(YamlPath.Parse(path), value));

    public static string Replace(string yaml, string path, string fragment) =>
        Apply(yaml, d => d.ReplaceValue(YamlPath.Parse(path), fragment));

    public static string Add(string yaml, string mapping, string key, string fragment) =>
        Apply(yaml, d => d.AddKey(YamlPath.Parse(mapping), key, fragment));

    public static string Remove(string yaml, string path) => Apply(yaml, d => d.RemoveKey(YamlPath.Parse(path)));

    public static string Crlf(string text) => text.Replace("\n", "\r\n");
}
