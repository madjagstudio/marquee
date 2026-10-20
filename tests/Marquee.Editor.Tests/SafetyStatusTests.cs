using System.Windows.Input;
using Marquee.Core.Themes;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;
using Marquee.Editor.Tests.TestSupport;
using static Marquee.Editor.Tests.TestSupport.OpenEditor;

namespace Marquee.Editor.Tests;

/// What the window offers when the model's safety steps in: a hand edit that isn't YAML, the problems list, a theme
/// folder that went away, and crash recovery that can't write.
public class SafetyStatusTests
{
    [Fact]
    public void A_file_that_isnt_yaml_pauses_with_revert_or_keep_editing_and_undo_off() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        using var editor = new OpenEditor(environment, mine);
        editor.Session.Select([P("widgets[1]")]);
        editor.Model.KeyDown(Key.Right, ModifierKeys.None);
        var text = editor.Session.Document.Text;

        File.WriteAllText(Path.Combine(mine, ThemeLoader.LayoutFileName), "widgets: [ { type: shape\n");
        Assert.True(Sta.PumpUntil(() => editor.Session.IsFileUnreadable));

        Assert.True(editor.Model.HasBanner);
        Assert.True(editor.Model.CanRevert);
        Assert.True(editor.Model.CanKeepEditing);
        Assert.False(editor.Model.UndoCommand.CanExecute(null));
        Assert.False(editor.Model.RedoCommand.CanExecute(null));
        editor.Model.KeyDown(Key.Z, ModifierKeys.Control);
        Assert.Equal(text, editor.Session.Document.Text);
        Assert.True(editor.Session.IsFileUnreadable);

        editor.Model.KeepEditingCommand.Execute(null);

        Assert.False(editor.Model.HasBanner);
        Assert.True(editor.Session.IsPaused);
        Assert.NotEmpty(editor.Model.Problems);
    });

    [Fact]
    public void Revert_from_the_banner_ends_a_pause_on_a_file_that_isnt_yaml() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        using var editor = new OpenEditor(environment, mine);

        File.WriteAllText(Path.Combine(mine, ThemeLoader.LayoutFileName), "widgets: [ { type: shape\n");
        Assert.True(Sta.PumpUntil(() => editor.Session.IsFileUnreadable));
        editor.Model.RevertCommand.Execute(null);

        Assert.False(editor.Session.IsPaused);
        Assert.False(editor.Model.HasBanner);
        Assert.Equal(TestEnvironment.Layout, editor.Session.Document.Text);
    });

    [Fact]
    public void A_problem_in_a_widget_selects_it() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        using var editor = new OpenEditor(environment, mine);

        File.WriteAllText(Path.Combine(mine, ThemeLoader.LayoutFileName), TestEnvironment.Layout.Replace("at: [500, 100]", "at: [500]"));
        Assert.True(Sta.PumpUntil(() => editor.Session.IsPaused));
        var problem = Assert.Single(editor.Model.Problems);
        Assert.Equal(Strings.OneProblem, editor.Model.ProblemsText);

        editor.Model.SelectProblemCommand.Execute(problem);

        Assert.Equal(P("widgets[1]"), problem.Widget);
        Assert.Equal([P("widgets[1]")], editor.Session.Selection);
    });

    [Fact]
    public void A_theme_folder_that_went_offers_save_to_a_folder_and_opens_it_from_there() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        var elsewhere = Path.Combine(environment.Root, "rescued");
        Directory.CreateDirectory(elsewhere);
        environment.Dialogs.FolderAnswer = elsewhere;
        using var editor = new OpenEditor(environment, mine);
        editor.Session.Select([P("widgets[1]")]);

        // The change, then the folder gone before autosave writes it, a second later.
        editor.Model.KeyDown(Key.Right, ModifierKeys.None);
        Directory.Delete(mine, recursive: true);
        Assert.True(Sta.PumpUntil(() => editor.Model.CanSaveToFolder, TimeSpan.FromSeconds(15)));
        Assert.Equal(Strings.NotSaved, editor.Model.SaveText);
        Assert.False(Directory.Exists(mine));

        editor.Model.SaveToFolderCommand.Execute(null);

        Assert.Equal(1, environment.Dialogs.FolderQuestions);
        Assert.Equal(elsewhere, editor.Model.Workspace!.Session.Folder);
        Assert.Contains("at: [501, 100]", File.ReadAllText(Path.Combine(elsewhere, ThemeLoader.LayoutFileName)));
        Assert.Equal(Strings.Saved, editor.Model.SaveText);
    });

    [Fact]
    public void Save_to_a_folder_asks_before_replacing_a_layout_already_there() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        var other = environment.AddTheme("other", TestEnvironment.Layout.Replace("#FF0000", "#123456"));
        var otherLayout = File.ReadAllText(Path.Combine(other, ThemeLoader.LayoutFileName));
        environment.Dialogs.FolderAnswer = other;
        using var editor = new OpenEditor(environment, mine);
        editor.Session.Select([P("widgets[1]")]);
        editor.Model.KeyDown(Key.Right, ModifierKeys.None);
        Directory.Delete(mine, recursive: true);
        Assert.True(Sta.PumpUntil(() => editor.Model.CanSaveToFolder, TimeSpan.FromSeconds(15)));

        environment.Dialogs.ReplaceAnswer = false;
        editor.Model.SaveToFolderCommand.Execute(null);

        Assert.Equal([other], environment.Dialogs.ReplaceQuestions);
        Assert.Equal(otherLayout, File.ReadAllText(Path.Combine(other, ThemeLoader.LayoutFileName)));
        Assert.Equal(mine, editor.Model.Workspace!.Session.Folder);
        Assert.True(editor.Model.CanSaveToFolder);

        environment.Dialogs.ReplaceAnswer = true;
        editor.Model.SaveToFolderCommand.Execute(null);

        Assert.Equal(2, environment.Dialogs.ReplaceQuestions.Count);
        Assert.Equal(other, editor.Model.Workspace!.Session.Folder);
        Assert.Contains("at: [501, 100]", File.ReadAllText(Path.Combine(other, ThemeLoader.LayoutFileName)));
    });

    [Fact]
    public void Crash_recovery_that_cant_write_is_said_in_the_status_bar() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        // A file where the recovery folder should be: nothing can be written under it.
        Directory.CreateDirectory(environment.Environment.Folders.Root);
        File.WriteAllText(environment.Environment.Folders.Recovery, "");
        using var editor = new OpenEditor(environment, mine);
        Assert.Null(editor.Model.RecoveryProblem);

        editor.Session.Select([P("widgets[1]")]);
        editor.Model.KeyDown(Key.Right, ModifierKeys.None);

        Assert.True(Sta.PumpUntil(() => editor.Model.RecoveryProblem is not null));
        Assert.StartsWith(Strings.RecoveryOff("").TrimEnd(), editor.Model.RecoveryProblem);
    });
}
