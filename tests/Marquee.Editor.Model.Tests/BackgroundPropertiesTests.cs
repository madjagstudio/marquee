using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Background and canvas properties come from the catalog and use the existing property commands.
public class BackgroundPropertiesTests
{
    [Theory]
    [InlineData("fill: \"#102030\"", "fill", new[] { "opacity", "blend", "hidden", "locked", "name", "fill" })]
    [InlineData("image: images/a.png", "image", new[] { "opacity", "blend", "hidden", "locked", "name", "image", "fit" })]
    [InlineData("video: video/a.mp4", "video", new[] { "opacity", "blend", "hidden", "locked", "name", "video", "fit" })]
    [InlineData("shader: shaders/a.sksl", "shader", new[] { "opacity", "blend", "hidden", "locked", "name", "shader", "uniforms", "scale" })]
    public void A_layer_lists_its_kinds_keys(string layer, string kind, string[] rows)
    {
        var document = LayoutDocument.Open($"canvas: {{ orientation: landscape }}\nbackground:\n  - {layer}\nwidgets: []\n");

        var model = PropertyModel.ForLayer(document, YamlPath.Parse("background[0]"));

        Assert.Equal(PropertyTargetKind.Layer, model.Target);
        Assert.Equal(kind, model.Type);
        Assert.Equal(rows, model.Rows.Select(row => row.Name));
        Assert.Empty(model.Style);
        Assert.Null(model.StyleUse);
        Assert.Empty(model.Rules);
    }

    [Fact]
    public void A_layer_without_a_kind_lists_only_common_rows()
    {
        var document = LayoutDocument.Open("background: [ { name: Sky } ]\n");

        var model = PropertyModel.ForLayer(document, YamlPath.Parse("background[0]"));

        Assert.Equal(PropertyTargetKind.Layer, model.Target);
        Assert.Null(model.Type);
        Assert.Equal(["opacity", "blend", "hidden", "locked", "name"], model.Rows.Select(row => row.Name));
        Assert.Empty(model.Style);
        Assert.Null(model.StyleUse);
        Assert.Empty(model.Rules);
    }

    [Fact]
    public void Setting_a_layers_fill_writes_only_that_key()
    {
        const string layout = "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#102030\" # night\nwidgets: []\n";
        using var theme = TempTheme.With(layout);
        using var session = EditorSession.Open(theme.Folder);
        session.Select([YamlPath.Parse("background[0]")]);
        var model = PropertyModel.For(session);

        session.Apply(model.Set(model.Row("fill")!, YamlValue.Parse("\"#FF0000\"")));

        Assert.Equal("canvas: { orientation: landscape }\nbackground:\n  - fill: \"#FF0000\" # night\nwidgets: []\n", session.Document.Text);
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void With_nothing_selected_the_panel_is_the_canvas()
    {
        using var theme = TempTheme.With("canvas: { orientation: landscape, fps: 30 }\nwidgets: []\n");
        using var session = EditorSession.Open(theme.Folder);

        var model = PropertyModel.For(session);

        Assert.Equal(PropertyTargetKind.Canvas, model.Target);
        Assert.Equal(["canvas.orientation", "canvas.fps"], model.Rows.Select(row => row.Name));
        Assert.Empty(model.Style);
        Assert.Null(model.StyleUse);
        Assert.Empty(model.Rules);
        session.Apply(model.Set(model.Row("canvas.fps")!, YamlValue.Parse("60")));
        Assert.Contains("fps: 60", session.Document.Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reset_removes_an_optional_key_and_undo_restores_the_exact_text(bool layer)
    {
        const string layout = "canvas: { orientation: landscape, fps: 30 }\nbackground:\n  - fill: \"#102030\"\n    opacity: 0.5 # dim\nwidgets: []\n";
        using var theme = TempTheme.With(layout);
        using var session = theme.Open();
        if (layer) session.Select([YamlPath.Parse("background[0]")]);
        var model = PropertyModel.For(session);
        var row = model.Row(layer ? "opacity" : "canvas.fps")!;

        session.Apply(model.Reset(row));

        Assert.False(session.Document.Exists(YamlPath.Parse(layer ? "background[0].opacity" : "canvas.fps")));
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void A_locked_layers_rows_are_read_only()
    {
        var document = LayoutDocument.Open("canvas: { orientation: landscape }\nbackground:\n  - fill: '#102030'\n    locked: true\n");

        var model = PropertyModel.ForLayer(document, YamlPath.Parse("background[0]"));

        Assert.All(model.Rows, row => Assert.False(row.IsEditable));
        Assert.Throws<PropertyValueException>(() => model.Set(model.Row("fill")!, YamlValue.Parse("'#FFFFFF'")));
        Assert.Throws<PropertyValueException>(() => model.Reset(model.Row("locked")!));
    }

    [Theory]
    [InlineData("canvas: &screen { orientation: landscape }\n", true)]
    [InlineData("canvas: { orientation: landscape }\nbackground:\n  - &layer { fill: '#102030' }\n", false)]
    public void Anchored_owners_are_read_only_including_missing_properties(string layout, bool canvas)
    {
        var document = LayoutDocument.Open(layout);

        var model = canvas ? PropertyModel.ForCanvas(document) : PropertyModel.ForLayer(document, YamlPath.Parse("background[0]"));

        Assert.All(model.Rows, row => Assert.False(row.IsEditable));
    }
}
