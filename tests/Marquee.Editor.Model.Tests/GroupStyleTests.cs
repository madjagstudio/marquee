using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// A group passes its text keys and letter effects down to its children, as the renderer draws them (StyleSpec.Inherited),
/// but not its box keys: the panel shows them as what a child inherits, and copying a child's style takes them.
public class GroupStyleTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "styles:\n" +
        "  big: { size: 30, fill: \"#112233\" }\n" +
        "widgets:\n" +
        "  - type: group\n" +
        "    at: [0, 0]\n" +
        "    size: [400, 200]\n" +
        "    style: { extends: big, color: \"#FF0000\", radius: 4 }\n" +
        "    children:\n" +
        "      - { type: text, at: [0, 0], size: [100, 40], text: A }\n" +
        "      - type: group\n" +
        "        at: [0, 50]\n" +
        "        size: [200, 100]\n" +
        "        style: { weight: bold, size: 20 }\n" +
        "        children:\n" +
        "          - { type: text, at: [0, 0], size: [100, 40], text: B, style: { size: 12 } }\n" +
        "  - { type: text, at: [500, 0], size: [100, 40], text: C }\n";

    static YamlPath P(string path) => YamlPath.Parse(path);

    static PropertyModel Model(EditorSession session, string widget) => PropertyModel.Build(session.Document, [session.Widget(P(widget))!]);

    [Fact]
    public void A_child_shows_the_text_keys_its_groups_pass_down_as_inherited()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var a = Model(session, "widgets[0].children[0]");
        Assert.Equal(new YamlText("30"), a.Row("style.size")!.Inherited);
        Assert.Equal(new YamlText("#FF0000"), a.Row("style.color")!.Inherited);
        Assert.Null(a.Row("style.radius")?.Inherited);
        Assert.Null(a.Row("style.fill")?.Inherited);

        // The inner group's size is over the outer's; the outer's colour comes through it.
        var b = Model(session, "widgets[0].children[1].children[0]");
        Assert.Equal(new YamlText("20"), b.Row("style.size")!.Inherited);
        Assert.Equal(new YamlText("12"), b.Row("style.size")!.Value);
        Assert.Equal(new YamlText("bold"), b.Row("style.weight")!.Inherited);
        Assert.Equal(new YamlText("#FF0000"), b.Row("style.color")!.Inherited);

        Assert.Null(Model(session, "widgets[1]").Row("style.size")!.Inherited);
    }

    [Fact]
    public void Copying_a_childs_style_takes_what_its_groups_pass_down()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var clipboard = new StyleClipboard();

        clipboard.Copy(session.Document, session.Widget(P("widgets[0].children[1].children[0]"))!);
        Assert.Equal("{ size: 12, color: \"#FF0000\", weight: bold }", YamlWriter.Flow(clipboard.Style!));

        session.Apply(clipboard.Paste([session.Widget(P("widgets[1]"))!]));
        Assert.Contains("text: C, style: { size: 12, color: \"#FF0000\", weight: bold } }", session.Document.Text);
    }

    [Fact]
    public void Pasting_onto_a_child_leaves_what_its_group_already_gives_it()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var clipboard = new StyleClipboard();

        clipboard.Copy(session.Document, session.Widget(P("widgets[0].children[0]"))!);
        session.Select([P("widgets[0].children[0]")]);
        Assert.False(clipboard.Paste(session));
    }

    [Fact]
    public void What_a_group_passes_down_are_style_keys() =>
        Assert.All(StyleValues.PassedDown, name => Assert.NotNull(Marquee.Core.Themes.Schema.ThemeSchema.Style.Key(name)));
}
