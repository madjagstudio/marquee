using Marquee.Core.Themes;
using Marquee.Editor.Dialogs;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

/// The toolbar's file commands: add an image or video from a file, clean up unused files (deleting only those ticked),
/// and "New from an example" under a name the user gives.
public class FileCommandsTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: image, at: [0, 0], size: [64, 64], src: images/used.png }\n";

    [Fact]
    public void Add_image_imports_the_file_and_adds_a_widget_showing_it() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var folder = environment.AddTheme("mine");
        var picture = Path.Combine(environment.Root, "outside", "logo.png");
        TestEnvironment.WritePng(picture);
        environment.Dialogs.ImportAnswers[AssetKind.Image] = picture;
        using var editor = new OpenEditor(environment, folder);

        Assert.True(editor.Model.AddImageCommand.CanExecute(null));
        editor.Model.AddImageCommand.Execute(null);

        Assert.True(File.Exists(Path.Combine(folder, "images", "logo.png")));
        Assert.Contains("src: images/logo.png", editor.Session.Document.Text);
        Assert.Equal(1, editor.Session.History.Count);
        Assert.Empty(environment.Dialogs.Errors);
    });

    [Fact]
    public void A_file_the_import_refuses_says_why_and_changes_nothing() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var folder = environment.AddTheme("mine");
        var notAPicture = Path.Combine(environment.Root, "notes.png");
        File.WriteAllText(notAPicture, "not a picture");
        environment.Dialogs.ImportAnswers[AssetKind.Image] = notAPicture;
        using var editor = new OpenEditor(environment, folder);

        editor.Model.AddFile(AssetKind.Image);

        var error = Assert.Single(environment.Dialogs.Errors);
        Assert.StartsWith(Strings.ImportRefused("notes.png", "").TrimEnd(), error);
        Assert.False(Directory.Exists(Path.Combine(folder, "images")));
        Assert.Equal(TestEnvironment.Layout, editor.Session.Document.Text);
    });

    [Fact]
    public void The_clean_up_dialog_lists_unused_files_ticked_with_their_sizes() => Sta.Run(() =>
    {
        var dialog = new CleanUpViewModel([new UnusedFile("images/a.png", 2048), new UnusedFile("video/b.mp4", 3 * 1024 * 1024)]);

        Assert.Equal(["images/a.png", "video/b.mp4"], dialog.Chosen);
        Assert.Equal(Strings.SizeKilobytes("2"), dialog.Files[0].SizeText);
        Assert.Equal(Strings.DeleteFiles("2", CleanUpViewModel.Size(2048 + 3 * 1024 * 1024)), dialog.DeleteText);

        dialog.Files[1].IsChecked = false;
        Assert.Equal(["images/a.png"], dialog.Chosen);
        dialog.SelectNoneCommand.Execute(null);
        Assert.False(dialog.CanDelete);
        dialog.SelectAllCommand.Execute(null);
        Assert.Equal(2, dialog.Chosen.Count);
    });

    [Fact]
    public void Clean_up_deletes_only_the_files_ticked() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var folder = environment.AddTheme("mine", Layout);
        foreach (var name in new[] { "used.png", "a.png", "b.png" }) TestEnvironment.WritePng(Path.Combine(folder, "images", name));
        environment.Dialogs.UnusedAnswer = files =>
        {
            var dialog = new CleanUpViewModel(files);
            dialog.Files.Single(file => file.Path == "images/b.png").IsChecked = false;
            return dialog.Chosen;
        };
        using var editor = new OpenEditor(environment, folder);

        editor.Model.CleanUpCommand.Execute(null);

        Assert.Equal(["images/a.png", "images/b.png"], environment.Dialogs.UnusedOffered.Single().Select(file => file.Path));
        Assert.False(File.Exists(Path.Combine(folder, "images", "a.png")));
        Assert.True(File.Exists(Path.Combine(folder, "images", "b.png")));
        Assert.True(File.Exists(Path.Combine(folder, "images", "used.png")));
        Assert.Equal(Strings.FilesDeleted("1"), editor.Model.Message);
    });

    [Fact]
    public void Clean_up_with_nothing_unused_says_so_and_a_cancel_deletes_nothing() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var folder = environment.AddTheme("mine", Layout);
        TestEnvironment.WritePng(Path.Combine(folder, "images", "used.png"));
        using var editor = new OpenEditor(environment, folder);

        editor.Model.CleanUp();
        Assert.Equal([Strings.NoUnusedFiles], environment.Dialogs.Infos);
        Assert.Empty(environment.Dialogs.UnusedOffered);

        TestEnvironment.WritePng(Path.Combine(folder, "images", "spare.png"));
        editor.Model.CleanUp();
        Assert.Single(environment.Dialogs.UnusedOffered);
        Assert.True(File.Exists(Path.Combine(folder, "images", "spare.png")));
    });

    [Fact]
    public void New_from_an_example_copies_it_under_the_name_given_and_opens_the_copy() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var example = environment.AddTheme("example-plain", installed: true);
        var mine = environment.AddTheme("mine");
        environment.Dialogs.CopyNameAnswer = "My dash";
        using var editor = new OpenEditor(environment, mine);

        editor.Model.NewFromExampleCommand.Execute(example);

        Assert.Equal([null, null], environment.Dialogs.CopyNameChecks);
        Assert.Equal(Path.Combine(environment.UserThemes, "My dash"), editor.Model.Workspace!.Session.Folder);
        Assert.Equal(TestEnvironment.Layout, File.ReadAllText(Path.Combine(environment.UserThemes, "My dash", ThemeLoader.LayoutFileName)));

        // The same name again is refused by the check, and a cancel copies nothing.
        environment.Dialogs.CopyNameAnswer = null;
        editor.Model.NewFromExampleCommand.Execute(example);
        Assert.Null(environment.Dialogs.CopyNameChecks[^1]);
        Assert.Equal(2, Directory.GetDirectories(environment.UserThemes).Length);
        Assert.NotNull(environment.Environment.Opening.CopyNameProblem("my dash"));
    });

    [Fact]
    public void Revert_to_opened_version_is_in_the_theme_menu_and_asks_first() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", TestEnvironment.Layout));
        var revert = Assert.Single(editor.Model.ThemeActions(), choice => choice.Label == Strings.RevertMenu);
        Assert.Same(editor.Model.RevertCommand, revert.Command);
        editor.Session.Select([OpenEditor.P("widgets[1]")]);
        editor.Model.KeyDown(System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.None);
        var edited = editor.Session.Document.Text;

        // Not paused, no banner: it is there all the same.
        Assert.False(editor.Model.HasBanner);
        Assert.True(revert.Command.CanExecute(null));

        environment.Dialogs.RevertAnswer = false;
        revert.Command.Execute(null);
        Assert.Equal(["mine"], environment.Dialogs.RevertQuestions);
        Assert.Equal(edited, editor.Session.Document.Text);

        environment.Dialogs.RevertAnswer = true;
        revert.Command.Execute(null);
        Assert.Equal(TestEnvironment.Layout, editor.Session.Document.Text);
        // Undo brings the edits back.
        Assert.True(editor.Session.Undo());
        Assert.Equal(edited, editor.Session.Document.Text);
    });

    [Fact]
    public void Keep_editing_doesnt_hide_revert_to_opened_version() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine", TestEnvironment.Layout);
        using var editor = new OpenEditor(environment, mine);
        File.WriteAllText(Path.Combine(mine, ThemeLoader.LayoutFileName), "widgets: [ { type: shape\n");
        Assert.True(Sta.PumpUntil(() => editor.Session.IsFileUnreadable));

        editor.Model.KeepEditingCommand.Execute(null);

        Assert.False(editor.Model.HasBanner);
        Assert.True(editor.Model.RevertCommand.CanExecute(null));
        editor.Model.RevertCommand.Execute(null);
        Assert.Single(environment.Dialogs.RevertQuestions);
        Assert.False(editor.Session.IsPaused);
        Assert.Equal(TestEnvironment.Layout, editor.Session.Document.Text);
    });
}
