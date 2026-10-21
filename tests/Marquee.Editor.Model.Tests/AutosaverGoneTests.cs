using Marquee.Core.Themes;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// A theme folder, or its layout.yaml, moved, renamed or deleted while the editor has it open: autosave never makes
/// either again (a half theme under the old name), says so, keeps the text for recovery, and can save it to a folder
/// the user chooses.
public class AutosaverGoneTests
{
    [Fact]
    public void A_theme_folder_deleted_while_open_is_not_made_again_and_says_so()
    {
        // The review's R04: the folder used to come back holding only layout.yaml, under "Saved".
        using var theme = TempTheme.With(Saving.Layout);
        File.WriteAllText(Path.Combine(theme.Folder, "keep.png"), "x");
        using var saving = new Saving(theme);
        using var recovery = new RecoveryStore(saving.Session, saving.Folders, saving.Ui, saving.Clock);
        saving.Move(20);
        Directory.Delete(theme.Folder, recursive: true);

        saving.Advance(1.5);

        Assert.False(Directory.Exists(theme.Folder));
        var state = saving.Autosaver.State;
        Assert.Equal(SaveStatus.Failed, state.Status);
        Assert.Equal(SaveProblem.ThemeGone, state.Problem);
        Assert.Contains("folder is gone", state.Reason);
        Assert.Equal(Saving.Moved(20), saving.Session.Document.Text);
        Assert.Equal(Saving.Moved(20), File.ReadAllText(recovery.Path));

        // Tried again later, in case it comes back, and never made meanwhile; a close keeps the text for recovery.
        saving.Advance(60);
        Assert.False(Directory.Exists(theme.Folder));
        Assert.False(saving.Autosaver.Flush());
        Assert.False(Directory.Exists(theme.Folder));
    }

    [Fact]
    public void A_layout_renamed_away_while_open_is_not_made_again_and_is_saved_once_it_is_back()
    {
        using var saving = new Saving();
        var away = Path.Combine(saving.Theme.Folder, "layout-old.yaml");
        saving.Move(20);
        File.Move(saving.Theme.LayoutPath, away);

        saving.Advance(1);

        Assert.False(File.Exists(saving.Theme.LayoutPath));
        Assert.Equal(SaveProblem.ThemeGone, saving.Autosaver.State.Problem);
        Assert.Contains($"{ThemeLoader.LayoutFileName} is gone", saving.Autosaver.State.Reason);

        File.Move(away, saving.Theme.LayoutPath);
        saving.Advance(2);
        Assert.Equal(Saving.Moved(20), saving.OnDisk);
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
    }

    [Fact]
    public void Save_to_writes_the_text_in_a_folder_the_user_chooses_and_leaves_nothing_waiting()
    {
        using var theme = TempTheme.With(Saving.Layout);
        using var chosen = new TempFolder();
        using var saving = new Saving(theme);
        saving.Move(20);
        Directory.Delete(theme.Folder, recursive: true);
        saving.Advance(1);

        var written = saving.Autosaver.SaveTo(chosen.Path)!;

        Assert.Equal(Path.Combine(chosen.Path, ThemeLoader.LayoutFileName), written);
        Assert.Equal(Saving.Moved(20), File.ReadAllText(written));
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
        Assert.Equal(0, saving.Clock.Armed);
        // A clean close follows, so recovery can let the text go.
        Assert.True(saving.Autosaver.Flush());
        Assert.False(Directory.Exists(theme.Folder));
        Assert.Empty(Directory.EnumerateFiles(chosen.Path, "*.tmp"));
    }

    [Fact]
    public void Save_to_replaces_a_layout_in_the_chosen_folder_only_when_told_and_never_makes_the_folder()
    {
        using var chosen = new TempFolder();
        File.WriteAllText(chosen.Combine(ThemeLoader.LayoutFileName), "old");
        using var saving = new Saving();
        saving.Move(20);

        // The shell asks the user first: another theme's layout.yaml is theirs to keep.
        Assert.Null(saving.Autosaver.SaveTo(chosen.Path));
        Assert.Equal("old", File.ReadAllText(chosen.Combine(ThemeLoader.LayoutFileName)));
        Assert.Equal(SaveStatus.Saving, saving.Autosaver.State.Status);
        Assert.Empty(Directory.EnumerateFiles(chosen.Path, "*.tmp", new EnumerationOptions { AttributesToSkip = 0 }));

        Assert.Equal(chosen.Combine(ThemeLoader.LayoutFileName), saving.Autosaver.SaveTo(chosen.Path, replace: true));

        Assert.Equal(Saving.Moved(20), File.ReadAllText(chosen.Combine(ThemeLoader.LayoutFileName)));
        Assert.Throws<DirectoryNotFoundException>(() => saving.Autosaver.SaveTo(chosen.Combine("missing")));
        Assert.False(Directory.Exists(chosen.Combine("missing")));
    }
}
