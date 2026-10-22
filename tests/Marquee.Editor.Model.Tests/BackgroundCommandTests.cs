using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Layer edits are single undo steps that keep selection, comments and untouched bytes.
public class BackgroundCommandTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "# the sky\n" +
        "background:\n" +
        "  - name: Night\n    fill: \"#070A12\"\n" +
        "  - fill: { radial: [\"#5B86E522\", \"#00000000\"] }\n    blend: screen\n" +
        "widgets: []\n";

    [Fact]
    public void A_fill_layer_is_added_above_the_selected_one_and_selected()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        session.Select([YamlPath.Parse("background[0]")]);

        session.Apply(new AddLayer(LayerKindChoice.Fill, null, AddLayer.AddIndex(session)));

        Assert.Equal(YamlPath.Parse("background[1]"), session.SelectedLayer);
        Assert.Equal("#000000", Scalar(session, "background[1].fill"));
        Assert.Contains("# the sky", session.Document.Text);
        Assert.Null(session.LoadError);
        Assert.Equal(1, session.History.Count);
        Assert.Equal("Add layer", session.History.NextUndo?.Label);
        var after = session.Document.Text;
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
        Assert.Equal(YamlPath.Parse("background[0]"), session.SelectedLayer);
        Assert.True(session.Redo());
        Assert.Equal(after, session.Document.Text);
        Assert.Equal(YamlPath.Parse("background[1]"), session.SelectedLayer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("background: []\n")]
    public void The_first_layer_makes_or_fills_the_background_list(string background)
    {
        var layout = "canvas: { orientation: landscape }\n" + background + "widgets: []\n";
        using var theme = TempTheme.With(layout);
        using var session = theme.Open();

        session.Apply(new AddLayer(LayerKindChoice.Fill, null, AddLayer.AddIndex(session)));

        Assert.Equal("#000000", Scalar(session, "background[0].fill"));
        Assert.Null(session.LoadError);
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
    }

    [Fact]
    public void A_layer_moves_and_is_removed_as_one_step_each()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        session.Select([YamlPath.Parse("background[0]")]);

        session.Apply(new MoveLayer(YamlPath.Parse("background[0]"), 1));
        Assert.Equal("Night", Scalar(session, "background[1].name"));
        Assert.Equal(YamlPath.Parse("background[1]"), session.SelectedLayer);
        Assert.Equal("Move layer", session.History.NextUndo?.Label);
        session.Apply(new RemoveLayer(YamlPath.Parse("background[1]")));
        Assert.False(session.Document.Exists(YamlPath.Parse("background[1]")));
        Assert.Empty(session.Selection);
        Assert.Equal("Delete layer", session.History.NextUndo?.Label);
        Assert.Equal(2, session.History.Count);

        Assert.True(session.Undo());
        Assert.Equal(YamlPath.Parse("background[1]"), session.SelectedLayer);
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
        Assert.Equal(YamlPath.Parse("background[0]"), session.SelectedLayer);
    }

    [Fact]
    public void Switching_kind_keeps_the_common_keys_and_drops_the_old_kind()
    {
        using var theme = TempTheme.With(Layout);
        Assets.Png(Path.Combine(theme.Folder, "images", "sky.png"));
        using var session = theme.Open();
        var layer = YamlPath.Parse("background[1]");
        session.Select([layer]);

        session.Apply(new ChangeLayerKind(layer, LayerKindChoice.Image, "images/sky.png"));

        Assert.Equal("images/sky.png", Scalar(session, "background[1].image"));
        Assert.Equal("screen", Scalar(session, "background[1].blend"));
        Assert.False(session.Document.Exists(layer.Child("fill")));
        Assert.Equal(layer, session.SelectedLayer);
        Assert.Null(session.LoadError);
        Assert.Equal(1, session.History.Count);
        Assert.Equal("Change layer kind", session.History.NextUndo?.Label);
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void Removing_the_last_layer_is_allowed()
    {
        const string layout = "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#FF0000\"\nwidgets: []\n";
        using var theme = TempTheme.With(layout);
        using var session = theme.Open();

        session.Apply(new RemoveLayer(YamlPath.Parse("background[0]")));

        Assert.Null(session.LoadError);
        Assert.Empty(session.Selection);
        Assert.Equal(0, session.Document.GetNode(YamlPath.Parse("background"))?.Count);
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
    }

    [Fact]
    public void With_a_widget_selected_a_new_layer_goes_on_top_and_replaces_selection()
    {
        var layout = Layout.Replace("widgets: []", "widgets: [{ type: text, at: [0, 0], size: [100, 20], text: Hi }]");
        using var theme = TempTheme.With(layout);
        using var session = theme.Open();
        var widget = YamlPath.Parse("widgets[0]");
        session.Select([widget]);

        Assert.Equal(2, AddLayer.AddIndex(session));
        session.Apply(new AddLayer(LayerKindChoice.Fill, null, AddLayer.AddIndex(session)));

        Assert.Equal(YamlPath.Parse("background[2]"), session.SelectedLayer);
        Assert.True(session.Undo());
        Assert.Equal([widget], session.Selection);
        Assert.Equal(layout, session.Document.Text);
    }

    [Theory]
    [InlineData(LayerKindChoice.Fill)]
    [InlineData(LayerKindChoice.Image)]
    [InlineData(LayerKindChoice.Video)]
    [InlineData(LayerKindChoice.Shader)]
    public void Switching_from_shader_resets_content_and_keeps_common_text(LayerKindChoice kind)
    {
        const string common = "    name: 'Sky' # keep name\r\n    opacity: 0.50\r\n    blend: screen\r\n    hidden: false\r\n    locked: false\r\n";
        const string layout = "canvas: { orientation: landscape }\r\n# sky\r\nbackground:\r\n" +
            "  - shader: shaders/old.sksl\r\n    uniforms: { strength: 0.5 }\r\n    scale: 0.25\r\n" + common + "widgets: []\r\n";
        using var theme = TempTheme.With(layout);
        WriteAsset(theme.Folder, LayerKindChoice.Shader, "shaders/old.sksl");
        var file = kind == LayerKindChoice.Fill ? null : AssetPath(kind);
        if (file is not null) WriteAsset(theme.Folder, kind, file);
        using var session = theme.Open();
        Assert.Null(session.LoadError);

        session.Apply(new ChangeLayerKind(YamlPath.Parse("background[0]"), kind, file));

        // Removing the first keys moves name onto the dash, but keeps its quoting, comment and all following bytes.
        Assert.Contains("  - " + common[4..], session.Document.Text);
        Assert.StartsWith("canvas: { orientation: landscape }\r\n# sky\r\nbackground:\r\n", session.Document.Text);
        Assert.EndsWith("widgets: []\r\n", session.Document.Text);
        var key = kind.ToString().ToLowerInvariant();
        Assert.Equal(file ?? "#000000", Scalar(session, $"background[0].{key}"));
        foreach (var oldKey in new[] { "fill", "image", "video", "shader", "uniforms", "scale", "fit" }.Where(k => k != key))
            Assert.False(session.Document.Exists(YamlPath.Parse($"background[0].{oldKey}")));
        Assert.Null(session.LoadError);
        Assert.Equal(1, session.History.Count);
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
    }

    [Theory]
    [InlineData(LayerKindChoice.Image)]
    [InlineData(LayerKindChoice.Video)]
    public void Switching_media_to_fill_removes_fit_in_a_flow_mapping(LayerKindChoice kind)
    {
        var key = kind.ToString().ToLowerInvariant();
        var file = AssetPath(kind);
        var layout = $"canvas: {{ orientation: landscape }}\nbackground: [{{ {key}: '{file}', fit: contain, opacity: 0.50 }}]\nwidgets: []\n";
        using var theme = TempTheme.With(layout);
        WriteAsset(theme.Folder, kind, file);
        using var session = theme.Open();

        session.Apply(new ChangeLayerKind(YamlPath.Parse("background[0]"), LayerKindChoice.Fill, null));

        Assert.Equal("#000000", Scalar(session, "background[0].fill"));
        Assert.Contains("opacity: 0.50", session.Document.Text);
        Assert.False(session.Document.Exists(YamlPath.Parse("background[0].fit")));
        Assert.Null(session.LoadError);
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
    }

    [Theory]
    [InlineData(LayerKindChoice.Image)]
    [InlineData(LayerKindChoice.Video)]
    [InlineData(LayerKindChoice.Shader)]
    public void Import_adds_above_selection_in_one_step_and_undo_keeps_the_file(LayerKindChoice kind)
    {
        using var theme = TempTheme.With(Layout);
        using var downloads = new TempFolder();
        var file = AssetPath(kind);
        var source = WriteAsset(downloads.Path, kind, file);
        using var session = theme.Open();
        session.Select([YamlPath.Parse("background[0]")]);

        var imported = new AssetImport(theme.Folder, Assets.Checks).AddLayer(session, kind, source);

        Assert.True(imported.Copied);
        Assert.Equal(file, imported.Path);
        Assert.Equal(file, Scalar(session, $"background[1].{kind.ToString().ToLowerInvariant()}"));
        Assert.Equal(YamlPath.Parse("background[1]"), session.SelectedLayer);
        Assert.Equal(1, session.History.Count);
        Assert.Equal("Add layer", session.History.NextUndo?.Label);
        Assert.Null(session.LoadError);
        var after = session.Document.Text;
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
        Assert.True(File.Exists(Path.Combine(theme.Folder, file)));
        Assert.True(session.Redo());
        Assert.Equal(after, session.Document.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_refused_add_discards_only_a_new_copy(bool alreadyInTheme)
    {
        var layout = "canvas: { orientation: landscape }\nbackground:\n" +
            string.Concat(Enumerable.Repeat("  - fill: \"#000000\"\n", 16)) + "widgets: []\n";
        using var theme = TempTheme.With(layout);
        using var downloads = new TempFolder();
        var source = Assets.Png(Path.Combine(alreadyInTheme ? theme.Folder : downloads.Path, "sky.png"));
        using var session = theme.Open();

        Assert.Throws<ThemeException>(() => new AssetImport(theme.Folder, Assets.Checks)
            .AddLayer(session, LayerKindChoice.Image, source));

        Assert.Equal(layout, session.Document.Text);
        Assert.False(session.CanUndo);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(theme.Folder, "images", "sky.png")));
    }

    [Theory]
    [InlineData("background[0]", "locked: true")]
    [InlineData("background[0]", "")]
    [InlineData("widgets[0]", "")]
    [InlineData("background[8]", "")]
    public void Locked_anchored_and_non_background_targets_are_refused(string path, string locked)
    {
        var layout = "canvas: { orientation: landscape }\nbackground:\n  - " +
            (path == "background[0]" && locked == "" ? "&sky " : "") +
            "{ fill: \"#000000\"" + (locked == "" ? "" : ", " + locked) + " }\n" +
            "widgets: [{ type: text, at: [0, 0], size: [100, 20], text: Hi }]\n";
        using var theme = TempTheme.With(layout);
        using var session = theme.Open();
        var layer = YamlPath.Parse(path);
        EditorCommand[] commands = [new RemoveLayer(layer), new MoveLayer(layer, 0), new ChangeLayerKind(layer, LayerKindChoice.Fill, null)];

        foreach (var command in commands)
        {
            if (path == "background[0]") Assert.Throws<LayoutEditException>(() => session.Apply(command));
            else Assert.Throws<ArgumentException>(() => session.Apply(command));
            Assert.Equal(layout, session.Document.Text);
            Assert.False(session.CanUndo);
        }
    }

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    [InlineData("example-agents")]
    [InlineData("example-showcase")]
    public void Example_themes_accept_layer_edits_and_undo_restores_every_byte(string example)
    {
        using var theme = TempTheme.Example(example);
        Assets.Png(Path.Combine(theme.Folder, "images", "task3.png"));
        using var session = theme.Open();
        Assert.Null(session.LoadError);
        var before = session.Document.Text;
        var index = AddLayer.AddIndex(session);

        session.Apply(new AddLayer(LayerKindChoice.Fill, null, index));
        session.Apply(new MoveLayer(YamlPath.Root.Child("background").Item(index), 0));
        session.Apply(new ChangeLayerKind(YamlPath.Parse("background[0]"), LayerKindChoice.Image, "images/task3.png"));
        session.Apply(new RemoveLayer(YamlPath.Parse("background[0]")));

        Assert.Null(session.LoadError);
        Assert.Equal(4, session.History.Count);
        for (var i = 0; i < 4; i++) Assert.True(session.Undo());
        Assert.Equal(before, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    static string AssetPath(LayerKindChoice kind) => kind switch
    {
        LayerKindChoice.Image => "images/sky #2.png",
        LayerKindChoice.Video => "video/sky #2.mp4",
        LayerKindChoice.Shader => "shaders/sky #2.sksl",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    static string WriteAsset(string folder, LayerKindChoice kind, string relative)
    {
        var path = Path.Combine(folder, relative);
        if (kind == LayerKindChoice.Image) return Assets.Png(path);
        if (kind == LayerKindChoice.Video) return Assets.Clip(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "uniform float strength; half4 main(float2 p) { return half4(strength, 0, 0, 1); }");
        return path;
    }

    static string? Scalar(EditorSession session, string path) =>
        session.Document.TryGetScalar(YamlPath.Parse(path), out var value) ? value : null;
}
