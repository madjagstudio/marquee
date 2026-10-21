using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// Editability is per node: a widget with an alias in one property isn't editable as a whole (it can't be removed,
/// moved or replaced), but its other properties are, and a property panel asks for each.
public class LayoutDocumentEditableKeysTests
{
    const string Anchored =
        "styles:\n" +
        "  base: &base { size: 20, color: red }\n" +
        "  gap: &gap 8\n" +
        "widgets:\n" +
        "  - { type: text, at: [*gap, 0], text: A, style: *base }\n" +
        "  - type: text\n" +
        "    at: [0, 0]\n";

    static LayoutDocument Document => LayoutDocument.Open(Anchored);

    [Fact]
    public void A_widget_with_an_alias_in_it_is_not_editable_but_its_other_properties_are()
    {
        Assert.False(Document.IsEditable(YamlPath.Parse("widgets[0]")));
        Assert.Equal(["type", "text"], Document.EditableKeys(YamlPath.Parse("widgets[0]")));
        Assert.Equal(
            Anchored.Replace("text: A,", "text: B,"),
            Edits.Set(Anchored, "widgets[0].text", "B"));
    }

    [Theory]
    [InlineData("widgets[1]", new[] { "type", "at" })]
    [InlineData("styles", new string[0])]
    [InlineData("styles.base", new string[0])]
    [InlineData("widgets", new string[0])]
    [InlineData("widgets[1].type", new string[0])]
    [InlineData("nothing", new string[0])]
    public void Lists_the_keys_whose_values_can_be_edited(string path, string[] keys) =>
        Assert.Equal(keys, Document.EditableKeys(YamlPath.Parse(path)));
}
