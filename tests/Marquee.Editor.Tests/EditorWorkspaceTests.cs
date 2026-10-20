using Marquee.Core.Themes;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;
using Marquee.Editor.Session;
using Marquee.Editor.Startup;
using Marquee.Editor.Tests.TestSupport;
using static Marquee.Editor.Tests.TestSupport.OpenEditor;

namespace Marquee.Editor.Tests;

/// Opening a theme wires the model's saving and safety in its order, with the dialogs where the user decides, and
/// closing it saves and cleans up.
public class EditorWorkspaceTests
{
    static SynchronizationContext Ui => SynchronizationContext.Current!;

    [Fact]
    public void A_theme_that_can_be_saved_opens_with_autosave_and_a_backup() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var workspace = EditorWorkspace.Open(environment.AddTheme("mine"), environment.Environment, Ui)!;

        Assert.False(workspace.IsReadOnly);
        Assert.NotNull(workspace.Autosaver);
        Assert.NotNull(workspace.Backup);
        Assert.Empty(environment.Dialogs.CopyQuestions);
        Assert.Empty(environment.Dialogs.RestoreOffers);
    });

    [Fact]
    public void Unsaved_changes_left_behind_are_offered_before_the_first_change_and_restored_as_a_step() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        var recovered = TestEnvironment.Layout.Replace("#0000FF", "#FFFF00");
        var recovery = environment.Environment.Folders.RecoveryFile(mine);
        Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
        File.WriteAllText(recovery, recovered);
        environment.Dialogs.RestoreAnswer = true;

        using var workspace = EditorWorkspace.Open(mine, environment.Environment, Ui)!;

        Assert.Equal(recovered, Assert.Single(environment.Dialogs.RestoreOffers).Text);
        Assert.Equal(recovered, workspace.Session.Document.Text);
        Assert.Equal(RecoveryOffer.RestoreLabel, workspace.Session.History.NextUndo!.Label);
    });

    [Fact]
    public void Declining_the_restore_keeps_layout_yaml() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        var recovery = environment.Environment.Folders.RecoveryFile(mine);
        Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
        File.WriteAllText(recovery, TestEnvironment.Layout.Replace("#0000FF", "#FFFF00"));

        using var workspace = EditorWorkspace.Open(mine, environment.Environment, Ui)!;

        Assert.Single(environment.Dialogs.RestoreOffers);
        Assert.Equal(TestEnvironment.Layout, workspace.Session.Document.Text);
        Assert.False(workspace.Session.CanUndo);
    });

    [Fact]
    public void An_installed_example_offers_a_copy_to_edit() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var example = environment.AddTheme("example-landscape", installed: true);
        environment.Dialogs.CopyAnswer = CopyChoice.MakeCopy;

        using var workspace = EditorWorkspace.Open(example, environment.Environment, Ui)!;

        Assert.Equal(["example-landscape"], environment.Dialogs.CopyQuestions);
        Assert.Equal(Path.Combine(environment.UserThemes, "example-landscape-copy"), workspace.Session.Folder);
        Assert.False(workspace.IsReadOnly);
        // The copy is this editor's; the example it came from is let go.
        Assert.Null(ThemeOpening.TryLock(workspace.Session.Folder));
        using (var themeLock = ThemeOpening.TryLock(example)) Assert.NotNull(themeLock);
    });

    [Fact]
    public void A_read_only_theme_open_already_is_brought_forward_without_asking_about_a_copy() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var example = environment.AddTheme("example-landscape", installed: true);
        environment.Dialogs.CopyAnswer = CopyChoice.ViewOnly;
        using var first = EditorWorkspace.Open(example, environment.Environment, Ui)!;
        var asked = 0;
        first.ShowRequested += (_, _) => asked++;

        var second = EditorWorkspace.Open(example, environment.Environment, Ui);

        Assert.Null(second);
        Assert.Equal(["example-landscape"], environment.Dialogs.CopyQuestions);
        Assert.True(Sta.PumpUntil(() => asked == 1));
    });

    [Fact]
    public void An_installed_example_can_be_viewed_read_only_with_nothing_written() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var example = environment.AddTheme("example-landscape", installed: true);
        environment.Dialogs.CopyAnswer = CopyChoice.ViewOnly;

        var workspace = EditorWorkspace.Open(example, environment.Environment, Ui)!;
        workspace.Close();

        Assert.True(workspace.IsReadOnly);
        Assert.Null(workspace.Autosaver);
        Assert.Null(workspace.Backup);
        Assert.False(Directory.Exists(environment.Environment.Folders.Root));
    });

    [Fact]
    public void Cancelling_the_copy_opens_nothing() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var example = environment.AddTheme("example-landscape", installed: true);

        Assert.Null(EditorWorkspace.Open(example, environment.Environment, Ui));
        using (var themeLock = ThemeOpening.TryLock(example)) Assert.NotNull(themeLock);
    });

    [Fact]
    public void A_theme_open_already_brings_its_window_forward_instead() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        using var first = EditorWorkspace.Open(mine, environment.Environment, Ui)!;
        var asked = 0;
        first.ShowRequested += (_, _) => asked++;

        var second = EditorWorkspace.Open(mine, environment.Environment, Ui);

        Assert.Null(second);
        Assert.True(Sta.PumpUntil(() => asked == 1));
        Assert.Empty(environment.Dialogs.AlreadyOpen);
    });

    [Fact]
    public void Without_a_window_to_bring_forward_the_signal_isnt_sent()
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");

        Assert.False(WindowSignal.Send(mine, TimeSpan.Zero));
    }

    [Fact]
    public void A_theme_that_doesnt_open_is_said_and_lets_go_of_its_lock() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var broken = environment.AddTheme("broken", "widgets: [ { type: shape\n");

        Assert.Null(EditorWorkspace.Open(broken, environment.Environment, Ui));

        Assert.StartsWith(Strings.CouldNotOpen("broken", "").TrimEnd(), Assert.Single(environment.Dialogs.Errors));
        using (var themeLock = ThemeOpening.TryLock(broken)) Assert.NotNull(themeLock);
    });

    [Fact]
    public void Closing_saves_what_waits_deletes_the_recovery_copy_and_lets_go() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        var editor = new OpenEditor(environment, mine);
        editor.Session.Select([P("widgets[1]")]);
        editor.Model.KeyDown(System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.None);
        var recovery = environment.Environment.Folders.RecoveryFile(mine);
        Assert.True(Sta.PumpUntil(() => File.Exists(recovery)));

        Assert.True(editor.Model.TryClose());

        Assert.Contains("at: [501, 100]", File.ReadAllText(Path.Combine(mine, ThemeLoader.LayoutFileName)));
        Assert.False(File.Exists(recovery));
        Assert.True(File.Exists(environment.Environment.Folders.OpenedFile(mine)));
        using (var themeLock = ThemeOpening.TryLock(mine)) Assert.NotNull(themeLock);
        editor.Model.Dispose();
    });

    [Fact]
    public void Another_theme_opens_in_place_of_this_one_after_saving_it() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        var other = environment.AddTheme("other");
        using var editor = new OpenEditor(environment, mine);
        editor.Session.Select([P("widgets[1]")]);
        editor.Model.KeyDown(System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.None);

        editor.Model.OpenThemeCommand.Execute(other);

        Assert.Equal(other, editor.Model.Workspace!.Session.Folder);
        Assert.Equal("other", editor.Model.ThemeName);
        Assert.Contains("at: [501, 100]", File.ReadAllText(Path.Combine(mine, ThemeLoader.LayoutFileName)));
        using (var themeLock = ThemeOpening.TryLock(mine)) Assert.NotNull(themeLock);
    });

    [Fact]
    public void A_hand_edit_that_doesnt_load_pauses_with_a_banner_and_revert_puts_it_back() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        using var editor = new OpenEditor(environment, mine);

        File.WriteAllText(Path.Combine(mine, ThemeLoader.LayoutFileName), TestEnvironment.Layout.Replace("shape: rect", "shape: blob"));
        Assert.True(Sta.PumpUntil(() => editor.Session.IsPaused));

        Assert.True(editor.Model.HasBanner);
        Assert.True(editor.Model.CanRevert);
        Assert.Contains(editor.Session.LoadError!.ToString(), editor.Model.Banner);
        Assert.NotEmpty(editor.Model.Problems);
        Assert.Equal(editor.Session.Problems, editor.Model.Problems);

        editor.Model.RevertCommand.Execute(null);

        Assert.False(editor.Session.IsPaused);
        Assert.False(editor.Model.HasBanner);
        Assert.Equal(TestEnvironment.Layout, editor.Session.Document.Text);
    });

    [Fact]
    public void The_save_state_follows_autosave() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        Assert.Equal(Strings.Saved, editor.Model.SaveText);

        editor.Session.Select([P("widgets[1]")]);
        editor.Model.KeyDown(System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.None);

        Assert.True(Sta.PumpUntil(() => editor.Model.SaveText == Strings.Saving));
        Assert.True(Sta.PumpUntil(() => editor.Model.SaveText == Strings.Saved));
        Assert.True(editor.Model.TrayNotRunning || Sta.PumpUntil(() => editor.Model.TrayNotRunning));
    });
}
