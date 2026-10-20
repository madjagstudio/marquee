using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Parsing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// "Clean up unused files": the files in the theme folder that nothing in layout.yaml names, and deleting the ones
/// ticked, each checked again first. Hidden and system files, dot-folders, links and layout.yaml are never offered.
public class UnusedFilesTests
{
    /// A theme that names a file in every place one can be named: background image, video and shader layers, image and
    /// video widgets (inside a group, one written with a backslash and in other letter case), and font files in a named
    /// style no widget uses, a named style, an inline style, an `extends` style, a `when` rule's style, a text widget's
    /// spans, a value widget's spans, and the styles a gauge's tick labels and a donut's slice labels are drawn in.
    const string EveryPlace =
        "canvas: { orientation: landscape }\n" +
        "styles:\n" +
        "  heading: { font: fonts/heading.ttf, size: 20 }\n" +
        "  spare: { font: fonts/spare.otf }\n" +
        "background:\n" +
        "  - image: images/bg.png\n" +
        "  - video: video/loop.mp4\n" +
        "  - shader: shaders/glow.sksl\n" +
        "widgets:\n" +
        "  - type: group\n" +
        "    at: [0, 0]\n" +
        "    size: [800, 400]\n" +
        "    children:\n" +
        "      - { type: image, at: [0, 0], size: [100, 100], src: Images\\LOGO.png }\n" +
        "      - { type: video, at: [100, 0], size: [320, 180], src: ./video/clip.mp4 }\n" +
        "  - { type: text, at: [0, 0], size: [200, 40], text: A, style: heading }\n" +
        "  - { type: text, at: [0, 50], size: [200, 40], text: B, style: { font: fonts/inline.ttf } }\n" +
        "  - { type: text, at: [0, 100], size: [200, 40], text: C, style: { extends: heading, font: fonts/extended.ttf } }\n" +
        "  - type: value\n" +
        "    at: [0, 150]\n" +
        "    size: [200, 40]\n" +
        "    source: cpu.temp\n" +
        "    when:\n" +
        "      - { above: 85, style: { font: fonts/hot.ttf } }\n" +
        "  - type: text\n" +
        "    at: [0, 200]\n" +
        "    size: [200, 40]\n" +
        "    spans:\n" +
        "      - { text: CPU, style: { font: fonts/span.ttf } }\n" +
        "  - type: value\n" +
        "    at: [0, 250]\n" +
        "    size: [200, 40]\n" +
        "    source: cpu.temp\n" +
        "    spans:\n" +
        "      - { text: \"{0:0}\", style: { font: fonts/value-span.ttf } }\n" +
        "  - type: gauge\n" +
        "    at: [900, 0]\n" +
        "    size: [200, 200]\n" +
        "    source: cpu.load\n" +
        "    ticks: { major: 4, labels: \"{0:0}\" }\n" +
        "    style: { font: fonts/labels.ttf }\n" +
        "  - type: donut\n" +
        "    at: [1200, 0]\n" +
        "    size: [200, 200]\n" +
        "    slices:\n" +
        "      - { source: cpu.load, label: CPU }\n" +
        "      - { source: gpu.load, label: GPU }\n" +
        "    style: { font: fonts/donut.ttf }\n" +
        "  # - { type: image, at: [0, 0], size: [10, 10], src: images/commented.png }\n";

    static readonly string[] Named =
    [
        "fonts/donut.ttf", "fonts/extended.ttf", "fonts/heading.ttf", "fonts/hot.ttf", "fonts/inline.ttf", "fonts/labels.ttf",
        "fonts/spare.otf", "fonts/span.ttf", "fonts/value-span.ttf", "images/bg.png", "images/logo.png", "shaders/glow.sksl",
        "video/clip.mp4", "video/loop.mp4",
    ];

    static TempTheme EveryPlaceTheme(params string[] extra)
    {
        var theme = TempTheme.With(EveryPlace);
        foreach (var file in Named.Concat(extra)) Write(theme, file);
        return theme;
    }

    static string Write(TempTheme theme, string relative, string text = "x")
    {
        var path = Path.Combine(theme.Folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    static IEnumerable<string> Paths(IEnumerable<UnusedFile> files) => files.Select(file => file.Path);

    [Fact]
    public void The_fixture_is_a_theme()
    {
        // Every place it names a file is a place the parser reads one from.
        var spec = LayoutParser.Parse(EveryPlace);

        Assert.Equal(3, spec.Background.Count);
    }

    [Fact]
    public void A_file_named_anywhere_in_the_layout_is_used_and_the_rest_are_listed()
    {
        using var theme = EveryPlaceTheme("images/old.png", "images/commented.png", "fonts/unused.ttf", "notes.txt");

        var unused = UnusedFiles.List(theme.Folder, EveryPlace);

        Assert.Equal(["fonts/unused.ttf", "images/commented.png", "images/old.png", "notes.txt"], Paths(unused));
        Assert.All(unused, file => Assert.Equal(1, file.Bytes));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Each_example_uses_every_file_it_has(string example)
    {
        using var theme = TempTheme.Example(example);

        Assert.Empty(UnusedFiles.List(theme.Folder, theme.Text));

        Write(theme, "images/left-over.png");
        Assert.Equal(["images/left-over.png"], Paths(UnusedFiles.List(theme.Folder, theme.Text)));
    }

    public static TheoryData<string> Examples => new(TempTheme.Examples);

    [Fact]
    public void Layout_hidden_and_system_files_dot_folders_and_links_are_never_listed()
    {
        using var theme = TempTheme.With("canvas: { orientation: landscape }\n");
        using var elsewhere = new TempFolder();
        File.WriteAllText(elsewhere.Combine("far.png"), "x");
        Write(theme, ".git/config");
        Write(theme, "images/.layout.yaml.tmp");
        File.SetAttributes(Write(theme, "images/thumb.png"), FileAttributes.Hidden);
        File.SetAttributes(Write(theme, "desktop.ini"), FileAttributes.System);
        Write(theme, "cache/inside.png");
        File.SetAttributes(Path.Combine(theme.Folder, "cache"), FileAttributes.Directory | FileAttributes.Hidden);
        Write(theme, "images/listed.png");
        var link = Path.Combine(theme.Folder, "linked");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            Assert.Equal(["images/listed.png"], Paths(UnusedFiles.List(theme.Folder, theme.Text)));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void The_session_lists_against_its_own_text()
    {
        using var theme = TempTheme.With(Saving.Layout);
        Write(theme, "images/logo.png");
        using var session = theme.Open();
        Assert.Equal(["images/logo.png"], Paths(UnusedFiles.List(session)));

        session.Apply(new AddWidget("image"));
        Assert.Equal(["images/logo.png"], Paths(UnusedFiles.List(session)));

        session.Apply(new SetValue(YamlPath.Parse("widgets[1].src"), "images/logo.png"));
        Assert.Empty(UnusedFiles.List(session));
    }

    [Fact]
    public void Deletes_only_the_files_chosen()
    {
        using var theme = EveryPlaceTheme("images/old.png", "fonts/unused.ttf", "notes.txt");

        var results = UnusedFiles.Delete(theme.Folder, EveryPlace, ["images/old.png", @"fonts\unused.ttf"]);

        Assert.Equal([new FileDeletion("images/old.png", null), new FileDeletion(@"fonts\unused.ttf", null)], results);
        Assert.All(results, result => Assert.True(result.Deleted));
        Assert.Equal(["notes.txt"], Paths(UnusedFiles.List(theme.Folder, EveryPlace)));
        Assert.All(Named, file => Assert.True(File.Exists(Path.Combine(theme.Folder, file))));
    }

    [Fact]
    public void A_read_only_unused_file_is_deleted()
    {
        using var theme = EveryPlaceTheme("images/old.png");
        var file = Path.Combine(theme.Folder, "images", "old.png");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        var results = UnusedFiles.Delete(theme.Folder, EveryPlace, ["images/old.png"]);

        Assert.Equal([new FileDeletion("images/old.png", null)], results);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void A_file_the_layout_names_by_now_is_kept()
    {
        using var theme = EveryPlaceTheme("images/old.png", "images/older.png");
        var now = EveryPlace + "  - { type: image, at: [0, 0], size: [10, 10], src: images/old.png }\n";

        var results = UnusedFiles.Delete(theme.Folder, now, ["images/old.png", "images/older.png"]);

        Assert.Equal([new FileDeletion("images/old.png", "layout.yaml uses it now."), new FileDeletion("images/older.png", null)],
            results);
        Assert.True(File.Exists(Path.Combine(theme.Folder, "images", "old.png")));
    }

    [Fact]
    public void A_file_layout_yaml_on_disk_names_is_kept_too()
    {
        // A hand edit the editor hasn't reloaded yet.
        using var theme = EveryPlaceTheme("images/old.png");
        File.AppendAllText(theme.LayoutPath, "  - { type: image, at: [0, 0], size: [10, 10], src: images/old.png }\n");

        var results = UnusedFiles.Delete(theme.Folder, EveryPlace, ["images/old.png"]);

        Assert.Equal([new FileDeletion("images/old.png", "layout.yaml uses it now.")], results);
        Assert.True(File.Exists(Path.Combine(theme.Folder, "images", "old.png")));
    }

    [Fact]
    public void A_path_outside_the_theme_folder_is_refused()
    {
        using var theme = EveryPlaceTheme();
        using var elsewhere = new TempFolder();
        var outside = elsewhere.Combine("outside.png");
        File.WriteAllText(outside, "x");
        // Both name the same file: by its full path, and climbing out of the theme folder.
        var results = UnusedFiles.Delete(theme.Folder, EveryPlace, [outside, $"../{Path.GetFileName(elsewhere.Path)}/outside.png"]);

        Assert.All(results, result => Assert.Equal("It is outside the theme folder.", result.Refusal));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void A_file_through_a_junction_is_refused_and_what_it_leads_to_is_kept()
    {
        using var theme = EveryPlaceTheme();
        using var elsewhere = new TempFolder();
        var far = elsewhere.Combine("far.png");
        File.WriteAllText(far, "x");
        var link = Path.Combine(theme.Folder, "linked");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            var results = UnusedFiles.Delete(theme.Folder, EveryPlace, ["linked/far.png"]);

            Assert.Equal(
                [new FileDeletion("linked/far.png", "It is a link, or in a linked folder, so it may be a file somewhere else.")],
                results);
            Assert.True(File.Exists(far));
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    [Fact]
    public void Files_in_a_OneDrive_folder_are_listed_and_deleted()
    {
        // OneDrive puts a cloud placeholder's reparse point on the folders and files it syncs: the editor saves through
        // those (ReparseTags), so the clean-up lists and deletes through them too.
        using var theme = EveryPlaceTheme("images/old.png", "art/unused.png");
        var tags = new FakeReparseTags()
            .With(theme.Folder, FakeReparseTags.OneDrive)
            .With(Path.Combine(theme.Folder, "images"), FakeReparseTags.OneDrive)
            .With(Path.Combine(theme.Folder, "images", "old.png"), FakeReparseTags.OneDrive)
            .With(Path.Combine(theme.Folder, "art"), FakeReparseTags.OneDrive);

        Assert.Equal(["art/unused.png", "images/old.png"], Paths(UnusedFiles.List(theme.Folder, EveryPlace, tags)));

        var results = UnusedFiles.Delete(theme.Folder, EveryPlace, ["images/old.png", "art/unused.png"], tags);

        Assert.All(results, result => Assert.True(result.Deleted, result.Refusal));
        Assert.False(File.Exists(Path.Combine(theme.Folder, "images", "old.png")));
        Assert.False(File.Exists(Path.Combine(theme.Folder, "art", "unused.png")));
    }

    [Fact]
    public void A_file_or_folder_that_is_a_symbolic_link_is_neither_listed_nor_deleted_in_a_OneDrive_folder_either()
    {
        using var theme = EveryPlaceTheme("images/old.png", "linked/far.png", "spare.png");
        var tags = new FakeReparseTags()
            .With(theme.Folder, FakeReparseTags.OneDrive)
            .With(Path.Combine(theme.Folder, "images", "old.png"), FakeReparseTags.SymbolicLink)
            .With(Path.Combine(theme.Folder, "linked"), FakeReparseTags.SymbolicLink);

        Assert.Equal(["spare.png"], Paths(UnusedFiles.List(theme.Folder, EveryPlace, tags)));

        var results = UnusedFiles.Delete(theme.Folder, EveryPlace, ["images/old.png", "linked/far.png"], tags);

        Assert.All(results, result => Assert.Equal("It is a link, or in a linked folder, so it may be a file somewhere else.", result.Refusal));
        Assert.True(File.Exists(Path.Combine(theme.Folder, "images", "old.png")));
        Assert.True(File.Exists(Path.Combine(theme.Folder, "linked", "far.png")));
    }

    [Fact]
    public void Layout_hidden_and_missing_files_are_refused()
    {
        using var theme = EveryPlaceTheme();
        File.SetAttributes(Write(theme, "images/thumb.png"), FileAttributes.Hidden);
        Write(theme, ".git/config");

        var results = UnusedFiles.Delete(theme.Folder, EveryPlace, ["layout.yaml", "images/thumb.png", ".git/config", "gone.png", "images"]);

        Assert.Equal(
        [
            new FileDeletion("layout.yaml", "It is the theme's layout.yaml."),
            new FileDeletion("images/thumb.png", "It is hidden, or in a hidden folder, which clean-up leaves alone."),
            new FileDeletion(".git/config", "It is hidden, or in a hidden folder, which clean-up leaves alone."),
            new FileDeletion("gone.png", "It is no longer there."),
            new FileDeletion("images", "It is a folder, not a file."),
        ], results);
        Assert.True(File.Exists(theme.LayoutPath));
        Assert.True(File.Exists(Path.Combine(theme.Folder, "images", "thumb.png")));
    }

    [Fact]
    public void Text_that_is_not_YAML_lists_nothing_and_deletes_nothing()
    {
        using var theme = EveryPlaceTheme("images/old.png");

        Assert.Throws<LayoutEditException>(() => UnusedFiles.List(theme.Folder, "a: [\n"));
        Assert.Throws<LayoutEditException>(() => UnusedFiles.Delete(theme.Folder, "a: [\n", ["images/old.png"]));
        Assert.True(File.Exists(Path.Combine(theme.Folder, "images", "old.png")));
    }

    [Fact]
    public void The_session_deletes_against_its_own_text()
    {
        using var theme = TempTheme.With(Saving.Layout);
        var logo = Write(theme, "images/logo.png");
        using var session = theme.Open();
        session.Apply(new AddWidget("image"));
        session.Apply(new SetValue(YamlPath.Parse("widgets[1].src"), "images/logo.png"));

        Assert.Equal([new FileDeletion("images/logo.png", "layout.yaml uses it now.")], UnusedFiles.Delete(session, ["images/logo.png"]));
        Assert.True(File.Exists(logo));
    }
}
