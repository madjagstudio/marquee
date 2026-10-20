using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// The contract every caller relies on: Open and every edit throw LayoutEditException, and nothing else, for text they
/// can't read and edits they won't make, whatever the file holds.
public class LayoutDocumentExceptionTests
{
    /// The edit either works or is refused with LayoutEditException; any other exception fails the test.
    static void OnlyRefuses(string yaml, Func<LayoutDocument, LayoutDocument> edit)
    {
        var document = LayoutDocument.Open(yaml);
        try
        {
            edit(document);
        }
        catch (LayoutEditException)
        {
            Assert.Equal(yaml, document.Text);
        }
    }

    [Theory]
    [InlineData("- :\n")]
    [InlineData("tagged: !!s[tr 30\n")]
    public void Open_refuses_text_that_YamlDotNet_rejects_without_a_position(string yaml)
    {
        var ex = Assert.Throws<LayoutEditException>(() => LayoutDocument.Open(yaml));

        Assert.StartsWith("YAML syntax error", ex.Reason);
        Assert.DoesNotContain("(Parameter", ex.Reason);
    }

    [Theory]
    [InlineData("- :")]
    [InlineData("!!s[tr 30")]
    public void A_new_value_YamlDotNet_rejects_without_a_position_is_refused(string fragment)
    {
        Edits.Refused("a: 1\n", d => d.ReplaceValue(YamlPath.Parse("a"), fragment));
        Assert.Throws<SpliceException>(() => YamlFragment.Parse(fragment));
    }

    [Fact]
    public void An_edit_whose_result_YamlDotNet_rejects_without_a_position_is_refused()
    {
        // `:` is a mapping with an empty key; under `b:` it becomes `  :`, which YamlDotNet can't read.
        Edits.Refused("b: 1\n", d => d.ReplaceValue(YamlPath.Parse("b"), ":"));
        Edits.Refused("w: []\n", d => d.AddKey(YamlPath.Root, "new_key", ":"));
    }

    [Theory]
    [InlineData("a: 1\n? complex\n: v\n", "complex")]
    [InlineData("? complex\n: v\nb: 1\n", "complex")]
    public void Removing_a_complex_key_works_or_is_refused(string yaml, string key) =>
        OnlyRefuses(yaml, d => d.RemoveKey(YamlPath.Parse(key)));
}
