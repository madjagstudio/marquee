using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

public class RenameKeyTests
{
    [Fact]
    public void Renaming_a_key_keeps_its_value_comments_and_place()
    {
        var document = LayoutDocument.Open("styles:\n  # cards\n  meter: { color: \"#FFF\" } # main\n  label: { size: 12 }\nwidgets: []\n");

        var renamed = document.RenameKey(YamlPath.Parse("styles.meter"), "gauge");

        Assert.Equal("styles:\n  # cards\n  gauge: { color: \"#FFF\" } # main\n  label: { size: 12 }\nwidgets: []\n", renamed.Text);
        Assert.Throws<LayoutEditException>(() => document.RenameKey(YamlPath.Parse("styles.meter"), "label"));
    }

    [Theory]
    [InlineData("styles: { 'meter': { size: 12 }, label: {} }\r\n", "styles: { \"a,b\": { size: 12 }, label: {} }\r\n")]
    [InlineData("styles:\n  meter: # main\n    size: 12\n", "styles:\n  a,b: # main\n    size: 12\n")]
    public void Quoting_fits_the_mapping_and_the_value_is_untouched(string text, string expected) =>
        Assert.Equal(expected, LayoutDocument.Open(text).RenameKey(YamlPath.Parse("styles.meter"), "a,b").Text);

    [Theory]
    [InlineData("")]
    [InlineData("widgets[0]")]
    [InlineData("missing")]
    [InlineData("styles.missing")]
    public void Only_existing_mapping_keys_can_be_renamed(string path) =>
        Assert.Throws<LayoutEditException>(() => LayoutDocument.Open("styles: {}\nwidgets: [text]\n")
            .RenameKey(YamlPath.Parse(path), "gauge"));

    [Fact]
    public void Keeping_the_same_name_is_a_no_op()
    {
        var document = LayoutDocument.Open("'meter': 12\n");
        Assert.Same(document, document.RenameKey(YamlPath.Parse("meter"), "meter"));
    }
}
