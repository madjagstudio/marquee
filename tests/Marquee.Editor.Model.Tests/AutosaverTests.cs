using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Autosave: about a second after the last change, an atomic write of the session's bytes; a failed write is retried,
/// later each time, with the text kept; and nothing is saved while editing is paused.
public class AutosaverTests
{
    [Fact]
    public void It_saves_about_a_second_after_the_last_change()
    {
        using var saving = new Saving();

        saving.Move(20);
        Assert.Equal(SaveStatus.Saving, saving.Autosaver.State.Status);
        saving.Advance(0.9);
        saving.Move(30);
        saving.Advance(0.9);
        Assert.Equal(Saving.Layout, saving.OnDisk);
        Assert.Equal(SaveStatus.Saving, saving.Autosaver.State.Status);

        saving.Advance(0.1);

        Assert.Equal(Saving.Moved(30), saving.OnDisk);
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
    }

    [Fact]
    public void Its_state_changes_are_raised_on_the_ui_thread()
    {
        using var saving = new Saving();
        var states = new List<SaveStatus>();
        saving.Autosaver.StateChanged += (_, state) => states.Add(state.Status);

        saving.Move(20);
        saving.Advance(1);
        Assert.Empty(states);

        saving.Ui.RunPending();
        Assert.Equal([SaveStatus.Saving, SaveStatus.Saved], states);
    }

    [Fact]
    public void A_change_undone_before_the_save_writes_nothing()
    {
        using var saving = new Saving();
        var written = File.GetLastWriteTimeUtc(saving.Theme.LayoutPath);

        saving.Move(20);
        saving.Session.Undo();

        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
        Assert.Equal(0, saving.Clock.Armed);
        saving.Advance(5);
        Assert.Equal(written, File.GetLastWriteTimeUtc(saving.Theme.LayoutPath));
        Assert.False(File.Exists(saving.Backup.Path));
    }

    [Fact]
    public void A_locked_file_fails_keeps_the_text_and_is_retried_later_each_time()
    {
        using var saving = new Saving();
        saving.Move(20);

        using (new FileStream(saving.Theme.LayoutPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saving.Advance(1);
            var failed = saving.Autosaver.State;
            Assert.Equal(SaveStatus.Failed, failed.Status);
            Assert.Contains("layout.yaml", failed.Reason);
            Assert.Equal(saving.Clock.GetUtcNow() + TimeSpan.FromSeconds(1), failed.RetryAt);
            Assert.Equal(Saving.Moved(20), saving.Session.Document.Text);

            // The second failure waits twice as long.
            saving.Advance(1);
            Assert.Equal(saving.Clock.GetUtcNow() + TimeSpan.FromSeconds(2), saving.Autosaver.State.RetryAt);
        }

        Assert.Equal(Saving.Layout, saving.OnDisk);
        saving.Advance(1.9);
        Assert.Equal(Saving.Layout, saving.OnDisk);
        saving.Advance(0.1);

        Assert.Equal(Saving.Moved(20), saving.OnDisk);
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
        Assert.Empty(Directory.EnumerateFiles(saving.Theme.Folder, "*.tmp"));
    }

    [Fact]
    public void A_change_after_a_failure_stays_failed_until_a_save_gets_through()
    {
        using var saving = new Saving();
        saving.Move(20);
        using (new FileStream(saving.Theme.LayoutPath, FileMode.Open, FileAccess.Read, FileShare.None))
            saving.Advance(1);

        saving.Move(30);
        Assert.Equal(SaveStatus.Failed, saving.Autosaver.State.Status);
        saving.Advance(1);

        Assert.Equal(Saving.Moved(30), saving.OnDisk);
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
    }

    [Fact]
    public void Nothing_is_saved_while_editing_is_paused()
    {
        using var saving = new Saving();
        var broken = Saving.Layout.Replace("shape: rect", "shape: hexagon");

        saving.Move(20);
        saving.Session.Apply(new ReplaceDocument(broken, "Changed outside the editor"));
        Assert.True(saving.Session.IsPaused);
        saving.Advance(10);

        Assert.Equal(Saving.Layout, saving.OnDisk);
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
        Assert.False(File.Exists(saving.Backup.Path));

        // Undo waits while paused; text from outside that loads ends the pause, and is saved.
        Assert.False(saving.Session.Undo());
        saving.Advance(10);
        Assert.Equal(Saving.Layout, saving.OnDisk);
        saving.Session.Apply(new ReplaceDocument(Saving.Moved(30), "Changed outside the editor"));
        saving.Advance(1);
        Assert.Equal(Saving.Moved(30), saving.OnDisk);
    }

    [Fact]
    public void Nothing_is_saved_while_the_file_is_unreadable()
    {
        using var saving = new Saving();
        saving.Move(20);

        saving.Session.PauseOnUnreadableFile(new LoadError("YAML syntax error", 3));
        saving.Advance(10);

        Assert.Equal(Saving.Layout, saving.OnDisk);
        // The file is the hand edit's, which supersedes the change: closing has nothing to save.
        Assert.True(saving.Autosaver.Flush());
        Assert.Equal(Saving.Layout, saving.OnDisk);
    }

    [Fact]
    public void Flush_saves_now()
    {
        using var saving = new Saving();
        saving.Move(20);

        Assert.True(saving.Autosaver.Flush());

        Assert.Equal(Saving.Moved(20), saving.OnDisk);
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
        Assert.Equal(0, saving.Clock.Armed);
    }

    [Fact]
    public void Flush_waits_for_a_slow_save_only_so_long_and_says_it_is_not_done()
    {
        using var saving = new Saving();
        using var release = new ManualResetEventSlim();
        saving.Move(20);
        // A write stuck in the file system (a network share, a placeholder downloading while offline), held until the
        // test lets it go, so no timing decides the outcome.
        saving.Autosaver.BeforeWrite = () => release.Wait(TimeSpan.FromSeconds(30));

        Assert.False(saving.Autosaver.Flush(TimeSpan.Zero));

        // The save goes on by itself and lands; the close went ahead keeping the text for recovery meanwhile. A flush
        // that waits as long as it takes (the tests' default) sees it through.
        release.Set();
        Assert.True(saving.Autosaver.Flush());
        Assert.Equal(Saving.Moved(20), saving.OnDisk);
    }

    [Fact]
    public void Flush_saves_on_a_thread_of_its_own_so_a_busy_thread_pool_cannot_hold_it_back()
    {
        using var saving = new Saving();
        bool? onPool = null;
        saving.Move(20);
        saving.Autosaver.BeforeWrite = () => onPool = Thread.CurrentThread.IsThreadPoolThread;

        Assert.True(saving.Autosaver.Flush());

        Assert.False(onPool);
        Assert.Equal(Saving.Moved(20), saving.OnDisk);
    }

    [Fact]
    public void Flush_reports_a_failure_and_keeps_the_text_for_the_next()
    {
        using var saving = new Saving();
        saving.Move(20);

        using (new FileStream(saving.Theme.LayoutPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(saving.Autosaver.Flush());

        Assert.Equal(SaveStatus.Failed, saving.Autosaver.State.Status);
        Assert.True(saving.Autosaver.Flush());
        Assert.Equal(Saving.Moved(20), saving.OnDisk);
    }

    [Fact]
    public void A_byte_order_mark_is_kept_through_a_save()
    {
        using var theme = TempTheme.With("﻿" + Saving.Layout);
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(theme.LayoutPath)[..3]);
        using var saving = new Saving(theme);

        saving.Move(20);
        saving.Advance(1);

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("﻿" + Saving.Moved(20)), File.ReadAllBytes(theme.LayoutPath));
    }

    [Fact]
    public void The_opened_version_is_kept_before_the_first_save_only()
    {
        using var saving = new Saving();

        saving.Move(20);
        saving.Advance(1);
        Assert.Equal(Saving.Layout, File.ReadAllText(saving.Backup.Path));
        Assert.StartsWith(saving.Folders.Opened, saving.Backup.Path);

        saving.Move(30);
        saving.Advance(1);
        Assert.Equal(Saving.Moved(30), saving.OnDisk);
        Assert.Equal(Saving.Layout, File.ReadAllText(saving.Backup.Path));
    }

    [Fact]
    public void A_backup_that_cannot_be_written_fails_the_save()
    {
        using var saving = new Saving();
        // A file where the opened folder should be.
        Directory.CreateDirectory(saving.Folders.Root);
        File.WriteAllText(saving.Folders.Opened, "");

        saving.Move(20);
        saving.Advance(1);

        Assert.Equal(SaveStatus.Failed, saving.Autosaver.State.Status);
        Assert.Contains("opened version", saving.Autosaver.State.Reason);
        Assert.Equal(SaveProblem.Backup, saving.Autosaver.State.Problem);
        Assert.Equal(Saving.Layout, saving.OnDisk);

        File.Delete(saving.Folders.Opened);
        saving.Advance(1);
        Assert.Equal(Saving.Moved(20), saving.OnDisk);
        Assert.Equal(Saving.Layout, File.ReadAllText(saving.Backup.Path));
    }
}
