using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// "Add image", "Add video" and Import… on a file key: the copy and the YAML edit as one undo step. Undo takes the
/// reference away and leaves the file, which "Clean up unused files" can delete later.
public class AssetImportCommandTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "styles:\n" +
        "  meter: { size: 20 }\n" +
        "widgets:\n" +
        "  - { type: text, at: [10, 10], size: [200, 40], text: Hi }\n" +
        "  - { type: text, at: [10, 60], size: [200, 40], text: Hey, style: meter }\n" +
        "  - type: group\n" +
        "    at: [400, 100]\n" +
        "    size: [400, 200]\n" +
        "    children:\n" +
        "      - { type: text, at: [0, 0], size: [100, 20], text: A }\n";

    sealed class Setup : IDisposable
    {
        public Setup()
        {
            Theme = TempTheme.With(Layout);
            Session = Theme.Open();
            Import = new AssetImport(Theme.Folder, Assets.Checks);
        }

        public TempTheme Theme { get; }

        public TempFolder Downloads { get; } = new();

        public EditorSession Session { get; }

        public AssetImport Import { get; }

        public string InTheme(string relative) => Path.Combine(Theme.Folder, relative);

        public void Dispose()
        {
            Session.Dispose();
            Theme.Dispose();
            Downloads.Dispose();
        }
    }

    /// Undo puts the text back and keeps the file; redo puts the reference back.
    static void AssertUndoKeepsTheFile(Setup setup, string after, string file)
    {
        Assert.True(setup.Session.Undo());
        Assert.Equal(Layout, setup.Session.Document.Text);
        Assert.True(File.Exists(setup.InTheme(file)));
        Assert.False(setup.Session.CanUndo);

        Assert.True(setup.Session.Redo());
        Assert.Equal(after, setup.Session.Document.Text);
    }

    [Fact]
    public void Add_image_imports_the_file_and_adds_an_image_widget_in_one_step()
    {
        using var setup = new Setup();
        var source = Assets.Png(setup.Downloads.Combine("logo.png"));

        var imported = setup.Import.AddImage(setup.Session, source);

        // In block form, as the widget before it, the group, is written.
        var expected = Layout +
            "  - type: image\n" +
            "    at: [900, 180]\n" +
            "    size: [120, 120]\n" +
            "    src: images/logo.png\n";
        Assert.Equal("images/logo.png", imported.Path);
        Assert.Equal(expected, setup.Session.Document.Text);
        Assert.Null(setup.Session.LoadError);
        Assert.Equal([YamlPath.Parse("widgets[3]")], setup.Session.Selection);
        Assert.Equal("Add image", setup.Session.History.NextUndo?.Label);
        AssertUndoKeepsTheFile(setup, expected, @"images\logo.png");
    }

    [Fact]
    public void Add_video_imports_the_clip_and_adds_a_video_widget_inside_the_group_worked_in()
    {
        using var setup = new Setup();
        var source = Assets.Clip(setup.Downloads.Combine("intro.mp4"));

        var imported = setup.Import.AddVideo(setup.Session, source, YamlPath.Parse("widgets[2]"));

        var expected = Layout +
            "      - { type: video, at: [40, 10], size: [320, 180], src: video/intro.mp4 }\n";
        Assert.Equal("video/intro.mp4", imported.Path);
        Assert.Equal(expected, setup.Session.Document.Text);
        Assert.Null(setup.Session.LoadError);
        Assert.Equal("Add video", setup.Session.History.NextUndo?.Label);
        AssertUndoKeepsTheFile(setup, expected, @"video\intro.mp4");
    }

    [Fact]
    public void Importing_into_a_file_key_sets_it_and_adds_the_key_when_it_is_missing()
    {
        using var setup = new Setup();
        var source = Assets.Font(setup.Downloads.Combine("Display.ttf"));

        var imported = setup.Import.SetFile(setup.Session, YamlPath.Parse("widgets[0].style.font"), source);

        var expected = Layout.Replace("text: Hi }", "text: Hi, style: { font: fonts/Display.ttf } }");
        Assert.Equal("fonts/Display.ttf", imported.Path);
        Assert.Equal(expected, setup.Session.Document.Text);
        Assert.Null(setup.Session.LoadError);
        Assert.Equal("Import Display.ttf", setup.Session.History.NextUndo?.Label);
        AssertUndoKeepsTheFile(setup, expected, @"fonts\Display.ttf");
    }

    [Fact]
    public void Importing_into_a_set_file_key_replaces_the_path()
    {
        using var setup = new Setup();
        var first = setup.Import.AddImage(setup.Session, Assets.Png(setup.Downloads.Combine("logo.png")));
        var second = setup.Import.SetFile(setup.Session, YamlPath.Parse("widgets[3].src"),
            Assets.Png(setup.Downloads.Combine(@"new\logo.png")));

        Assert.Equal("images/logo-2.png", second.Path);
        Assert.EndsWith("    src: images/logo-2.png\n", setup.Session.Document.Text);
        Assert.True(setup.Session.Undo());
        Assert.EndsWith($"    src: {first.Path}\n", setup.Session.Document.Text);
        Assert.True(File.Exists(setup.InTheme(@"images\logo-2.png")));
    }

    [Fact]
    public void A_file_name_YAML_would_misread_is_quoted()
    {
        using var setup = new Setup();
        var source = Assets.Png(setup.Downloads.Combine("logo #2.png")); // a plain " #" starts a comment

        var imported = setup.Import.AddImage(setup.Session, source);

        Assert.Equal("images/logo #2.png", imported.Path);
        Assert.True(setup.Session.Document.TryGetScalar(YamlPath.Parse("widgets[3].src"), out var src));
        Assert.Equal("images/logo #2.png", src);
        Assert.Null(setup.Session.LoadError);
    }

    [Fact]
    public void A_refused_file_changes_nothing()
    {
        using var setup = new Setup();
        var source = setup.Downloads.Combine("broken.png");
        File.WriteAllText(source, "not an image");

        Assert.Throws<AssetImportException>(() => setup.Import.AddImage(setup.Session, source));

        Assert.Equal(Layout, setup.Session.Document.Text);
        Assert.False(setup.Session.CanUndo);
        Assert.False(Directory.Exists(setup.InTheme("images")));
    }

    [Fact]
    public void A_font_for_a_widget_with_a_named_style_is_written_as_an_override()
    {
        using var setup = new Setup();
        var source = Assets.Font(setup.Downloads.Combine("Display.ttf"));

        setup.Import.SetFile(setup.Session, YamlPath.Parse("widgets[1].style.font"), source);

        var expected = Layout.Replace("style: meter }", "style: { extends: meter, font: fonts/Display.ttf } }");
        Assert.Equal(expected, setup.Session.Document.Text);
        Assert.Null(setup.Session.LoadError);
        AssertUndoKeepsTheFile(setup, expected, @"fonts\Display.ttf");
    }

    [Fact]
    public void An_edit_the_file_refuses_removes_the_copy_it_made()
    {
        using var setup = new Setup();
        var source = Assets.Font(setup.Downloads.Combine("Display.ttf"));

        // A widget the file hasn't: the edit is refused once the copy is made.
        Assert.Throws<LayoutEditException>(() =>
            setup.Import.SetFile(setup.Session, YamlPath.Parse("widgets[9].style.font"), source));

        Assert.Equal(Layout, setup.Session.Document.Text);
        Assert.False(setup.Session.CanUndo);
        Assert.Empty(Directory.EnumerateFiles(setup.InTheme("fonts")));
    }

    [Fact]
    public void A_key_that_takes_no_importable_file_is_refused_before_anything_is_copied()
    {
        using var setup = new Setup();
        var source = Assets.Png(setup.Downloads.Combine("logo.png"));

        Assert.Throws<ArgumentException>(() => setup.Import.SetFile(setup.Session, YamlPath.Parse("widgets[0].text"), source));

        Assert.False(Directory.Exists(setup.InTheme("images")));
    }

    [Fact]
    public void An_import_for_another_theme_folder_is_refused()
    {
        using var setup = new Setup();
        using var other = TempTheme.With(Layout);
        var source = Assets.Png(setup.Downloads.Combine("logo.png"));

        Assert.Throws<ArgumentException>(() => new AssetImport(other.Folder, Assets.Checks).AddImage(setup.Session, source));

        Assert.False(Directory.Exists(Path.Combine(other.Folder, "images")));
    }
}
