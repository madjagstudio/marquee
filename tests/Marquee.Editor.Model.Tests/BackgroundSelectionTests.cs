using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// A background layer is selected on its own, and kept across undo while it exists.
public class BackgroundSelectionTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "background:\n  - fill: \"#102030\"\n  - fill: { linear: [\"#000000\", \"#FFFFFF\"] }\n" +
        "widgets:\n  - { type: text, at: [10, 10], size: [200, 40], text: hi }\n";

    static readonly YamlPath Layer1 = YamlPath.Parse("background[1]");
    static readonly YamlPath Widget0 = YamlPath.Parse("widgets[0]");

    [Fact]
    public void A_layer_is_selected_alone_and_replaces_the_widgets()
    {
        using var theme = TempTheme.With(Layout);
        using var session = EditorSession.Open(theme.Folder);
        session.Select([Widget0]);

        session.Select([Layer1]);

        Assert.Equal([Layer1], session.Selection);
        Assert.Equal(Layer1, session.SelectedLayer);
        Assert.Throws<ArgumentException>(() => session.Select([Layer1, Widget0]));
        Assert.Throws<ArgumentException>(() => session.Select([YamlPath.Parse("background[0]"), Layer1]));
        Assert.Throws<ArgumentException>(() => session.Select([YamlPath.Parse("background[5]")]));
        Assert.Equal([Layer1], session.Selection);

        session.Select([Widget0]);
        Assert.Equal([Widget0], session.Selection);
        Assert.Null(session.SelectedLayer);
        Assert.Equal(PropertyTargetKind.Widgets, PropertyModel.For(session).Target);
        session.Select([]);
        Assert.Null(session.SelectedLayer);
    }

    [Theory]
    [InlineData("background[1]", true)]
    [InlineData("background[5]", false)]
    [InlineData("background[1].fill", false)]
    [InlineData("background", false)]
    [InlineData("widgets[0]", false)]
    [InlineData("", false)]
    public void Only_existing_background_items_are_layers(string path, bool expected)
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();

        Assert.Equal(expected, session.IsBackgroundLayer(YamlPath.Parse(path)));
    }

    [Fact]
    public void A_selected_layer_stays_selected_through_an_edit_and_its_undo()
    {
        using var theme = TempTheme.With(Layout);
        using var session = EditorSession.Open(theme.Folder);
        session.Select([Layer1]);
        var model = PropertyModel.For(session);

        session.Apply(model.Set(model.Row("opacity")!, YamlValue.Parse("0.5")));
        Assert.Equal(Layer1, session.SelectedLayer);
        Assert.True(session.Undo());

        Assert.Equal(Layer1, session.SelectedLayer);
        Assert.Equal(Layout, session.Document.Text);
        Assert.True(session.Redo());
        Assert.Equal(Layer1, session.SelectedLayer);
    }

    [Fact]
    public void Removing_the_selected_layer_clears_selection_and_undo_restores_it()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        session.Select([Layer1]);

        session.Apply(new EditDocument("Remove layer", document => document.RemoveItem(Layer1)));

        Assert.Empty(session.Selection);
        Assert.Null(session.SelectedLayer);
        Assert.True(session.Undo());
        Assert.Equal(Layer1, session.SelectedLayer);
    }
}
