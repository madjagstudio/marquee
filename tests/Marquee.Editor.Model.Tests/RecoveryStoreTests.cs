using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Crash recovery: every change's text is written under the editor's root, deleted on a clean close, and offered back
/// on the next open when it differs from layout.yaml.
public class RecoveryStoreTests
{
    [Fact]
    public void A_change_is_written_shortly_after_and_a_run_of_changes_at_most_that_often()
    {
        using var saving = new Saving();
        using var recovery = new RecoveryStore(saving.Session, saving.Folders, saving.Ui, saving.Clock);
        Assert.Null(recovery.Offer);
        Assert.StartsWith(saving.Folders.Recovery, recovery.Path);

        saving.Move(20);
        Assert.False(File.Exists(recovery.Path));
        saving.Clock.Advance(RecoveryStore.DefaultDelay);
        Assert.Equal(Saving.Moved(20), File.ReadAllText(recovery.Path));

        // A run of changes (nudges): the text is written while it runs, not only when it stops.
        for (var x = 21; x <= 29; x++)
        {
            saving.Move(x);
            saving.Clock.Advance(RecoveryStore.DefaultDelay / 2);
        }

        Assert.Equal(Saving.Moved(28), File.ReadAllText(recovery.Path));
        saving.Move(30);
        saving.Clock.Advance(RecoveryStore.DefaultDelay);
        Assert.Equal(Saving.Moved(30), File.ReadAllText(recovery.Path));
    }

    [Fact]
    public void A_clean_close_deletes_it()
    {
        using var saving = new Saving();
        var recovery = new RecoveryStore(saving.Session, saving.Folders, saving.Ui, saving.Clock);
        saving.Move(20);
        saving.Clock.Advance(RecoveryStore.DefaultDelay);
        Assert.True(File.Exists(recovery.Path));

        recovery.CloseCleanly();

        Assert.False(File.Exists(recovery.Path));
        saving.Move(30);
        saving.Clock.Advance(RecoveryStore.DefaultDelay);
        Assert.False(File.Exists(recovery.Path));
    }

    [Fact]
    public void After_an_unclean_close_the_next_open_offers_it_with_both_times_and_restores_it_as_one_step()
    {
        using var data = new TempFolder();
        using var theme = TempTheme.With(Saving.Layout);
        DateTimeOffset written;
        using (var first = new Saving(theme, data))
        {
            var recovery = new RecoveryStore(first.Session, first.Folders, first.Ui, first.Clock);
            first.Move(20);
            first.Move(30);
            // Gone before autosave ran: Dispose is all a crash would leave, and writes what was waiting.
            recovery.Dispose();
            written = File.GetLastWriteTimeUtc(recovery.Path);
        }

        Assert.Equal(Saving.Layout, File.ReadAllText(theme.LayoutPath));
        using var second = new Saving(theme, data);
        using var again = new RecoveryStore(second.Session, second.Folders, second.Ui, second.Clock);

        var offer = Assert.IsType<RecoveryOffer>(again.Offer);
        Assert.Equal(Saving.Moved(30), offer.Text);
        Assert.Equal(written, offer.Saved.UtcDateTime);
        Assert.Equal(File.GetLastWriteTimeUtc(theme.LayoutPath), offer.LayoutSaved.UtcDateTime);

        second.Session.Apply(offer.Restore());

        Assert.Equal(Saving.Moved(30), second.Session.Document.Text);
        Assert.Equal(1, second.Session.History.Count);
        Assert.Equal(RecoveryOffer.RestoreLabel, second.Session.History.NextUndo!.Label);
        second.Advance(1);
        Assert.Equal(Saving.Moved(30), second.OnDisk);
    }

    [Fact]
    public void Text_that_was_saved_after_all_is_not_offered()
    {
        using var data = new TempFolder();
        using var theme = TempTheme.With(Saving.Layout);
        using (var first = new Saving(theme, data))
        {
            var recovery = new RecoveryStore(first.Session, first.Folders, first.Ui, first.Clock);
            first.Move(20);
            first.Advance(1);
            recovery.Dispose();
        }

        using var second = new Saving(theme, data);
        using var again = new RecoveryStore(second.Session, second.Folders, second.Ui, second.Clock);

        Assert.Null(again.Offer);
    }

    [Fact]
    public void A_write_that_fails_is_a_problem_the_shell_can_show_until_one_succeeds()
    {
        using var saving = new Saving();
        using var recovery = new RecoveryStore(saving.Session, saving.Folders, saving.Ui, saving.Clock);
        var raised = new List<string?>();
        recovery.ProblemChanged += (_, _) => raised.Add(recovery.Problem);
        // A file where the recovery folder should be.
        Directory.CreateDirectory(saving.Folders.Root);
        File.WriteAllText(saving.Folders.Recovery, "");

        saving.Move(20);
        saving.Clock.Advance(RecoveryStore.DefaultDelay);

        Assert.Contains("recovery", recovery.Problem);
        Assert.Empty(raised);
        saving.Ui.RunPending();
        Assert.Equal([recovery.Problem], raised);

        File.Delete(saving.Folders.Recovery);
        saving.Move(30);
        saving.Clock.Advance(RecoveryStore.DefaultDelay);
        saving.Ui.RunPending();

        Assert.Null(recovery.Problem);
        Assert.Equal(2, raised.Count);
        Assert.Equal(Saving.Moved(30), File.ReadAllText(recovery.Path));
    }

    [Fact]
    public void A_write_that_fails_is_tried_again_with_no_change_after_it()
    {
        using var saving = new Saving();
        using var recovery = new RecoveryStore(saving.Session, saving.Folders, saving.Ui, saving.Clock);
        Directory.CreateDirectory(saving.Folders.Root);
        File.WriteAllText(saving.Folders.Recovery, "");
        saving.Move(20);
        saving.Clock.Advance(RecoveryStore.DefaultDelay);
        Assert.NotNull(recovery.Problem);

        File.Delete(saving.Folders.Recovery);
        saving.Clock.Advance(RecoveryStore.RetryDelay);

        Assert.Null(recovery.Problem);
        Assert.Equal(Saving.Moved(20), File.ReadAllText(recovery.Path));
    }

    [Fact]
    public void A_close_after_a_write_that_failed_keeps_the_text_with_no_change_after_it()
    {
        // The QA review's R2: the failed write's text used to be dropped, so a close with no change after it kept nothing.
        using var saving = new Saving();
        var recovery = new RecoveryStore(saving.Session, saving.Folders, saving.Ui, saving.Clock);
        Directory.CreateDirectory(saving.Folders.Root);
        File.WriteAllText(saving.Folders.Recovery, "");
        saving.Move(20);
        saving.Clock.Advance(RecoveryStore.DefaultDelay);
        Assert.NotNull(recovery.Problem);

        File.Delete(saving.Folders.Recovery);
        recovery.Dispose();

        Assert.Equal(Saving.Moved(20), File.ReadAllText(recovery.Path));
    }

    [Fact]
    public void A_recovery_file_that_cannot_be_read_is_a_problem_not_a_silent_nothing()
    {
        using var data = new TempFolder();
        using var theme = TempTheme.With(Saving.Layout);
        var folders = new EditorFolders(data.Combine("editor"));
        Directory.CreateDirectory(folders.Recovery);
        // UTF-16, which the editor never writes and can't take.
        File.WriteAllBytes(folders.RecoveryFile(theme.Folder), [0xFF, 0xFE, (byte)'x', 0]);
        using var saving = new Saving(theme, data);

        using var recovery = new RecoveryStore(saving.Session, saving.Folders, saving.Ui, saving.Clock);

        Assert.Null(recovery.Offer);
        Assert.Contains("couldn't be read", recovery.Problem);
    }

    [Fact]
    public void A_byte_order_mark_is_kept_through_recovery()
    {
        using var data = new TempFolder();
        using var theme = TempTheme.With("﻿" + Saving.Layout);
        using (var first = new Saving(theme, data))
        {
            var recovery = new RecoveryStore(first.Session, first.Folders, first.Ui, first.Clock);
            first.Move(20);
            recovery.Dispose();
            Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(recovery.Path)[..3]);
        }

        using var second = new Saving(theme, data);
        using var again = new RecoveryStore(second.Session, second.Folders, second.Ui, second.Clock);
        second.Session.Apply(again.Offer!.Restore());

        Assert.Equal("﻿" + Saving.Moved(20), second.Session.Document.Text);
    }
}
