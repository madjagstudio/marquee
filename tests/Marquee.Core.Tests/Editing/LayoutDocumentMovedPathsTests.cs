using Marquee.Core.Themes.Editing;

namespace Marquee.Core.Tests.Editing;

/// The paths structural edits give back: where each item they moved is in the new document, for the editor's selection.
public class LayoutDocumentMovedPathsTests
{
    const string Yaml =
        "w:\n" +
        "  - { type: text, text: a }\n" +
        "  - type: group\n" +
        "    children:\n" +
        "      - { type: text, text: b }\n" +
        "      - { type: text, text: c }\n" +
        "  - { type: text, text: d }\n";

    static LayoutDocument Document => LayoutDocument.Open(Yaml);

    static string TextAt(LayoutDocument document, YamlPath path) =>
        document.TryGetScalar(path.Child("text"), out var text) ? text : "(none)";

    [Theory]
    [InlineData("w[0]", "w", 2, "w[2]")]
    [InlineData("w[2]", "w", 0, "w[0]")]
    [InlineData("w[0]", "w[1].children", 1, "w[0].children[1]")]
    [InlineData("w[2]", "w[1].children", 0, "w[1].children[0]")]
    [InlineData("w[1].children[1]", "w", 0, "w[0]")]
    [InlineData("w[1].children[0]", "w[1].children", 1, "w[1].children[1]")]
    [InlineData("w[0]", "w", 0, "w[0]")]
    public void MoveItem_gives_the_items_new_path(string item, string list, int index, string expected)
    {
        var before = Document;
        var after = before.MoveItem(YamlPath.Parse(item), YamlPath.Parse(list), index, out var moved);

        Assert.Equal(YamlPath.Parse(expected), moved);
        Assert.Equal(TextAt(before, YamlPath.Parse(item)), TextAt(after, moved));
    }

    [Fact]
    public void WrapInGroup_gives_each_items_new_path_in_the_order_asked()
    {
        var after = Document.WrapInGroup(YamlPath.Parse("w"), [2, 0], "type: group", out var moved);

        Assert.Equal([YamlPath.Parse("w[0].children[1]"), YamlPath.Parse("w[0].children[0]")], moved);
        Assert.Equal(["d", "a"], moved.Select(path => TextAt(after, path)));
    }

    [Fact]
    public void UnwrapGroup_gives_each_childs_new_path()
    {
        var after = Document.UnwrapGroup(YamlPath.Parse("w[1]"), out var moved);

        Assert.Equal([YamlPath.Parse("w[1]"), YamlPath.Parse("w[2]")], moved);
        Assert.Equal(["b", "c"], moved.Select(path => TextAt(after, path)));
    }

    [Fact]
    public void Unwrapping_an_empty_group_moves_nothing()
    {
        LayoutDocument.Open("w:\n  - { type: group, children: [] }\n").UnwrapGroup(YamlPath.Parse("w[0]"), out var moved);

        Assert.Empty(moved);
    }
}
