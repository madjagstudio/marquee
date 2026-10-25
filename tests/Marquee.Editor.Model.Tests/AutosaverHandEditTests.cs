using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Autosave never writes over a hand edit: it writes only while the file holds what the editor last wrote or read, and
/// only bytes made from what the session has taken from outside. Anything else goes to the external change watcher,
/// which reloads it as one undo step (or pauses). A session that moved on while a write was in flight is saved again.
public class AutosaverHandEditTests
{
    static readonly TimeSpan Debounce = ExternalChangeWatcher.DefaultDebounce;

    [Fact]
    public void A_hand_edit_saved_just_before_the_autosave_is_reloaded_not_written_over()
    {
        // The hand edit lands 0.1 s before the autosave, before the watcher reads it.
        using var saving = new Saving();
        using var watcher = new ExternalChangeWatcher(saving.Session, saving.File, saving.Ui, saving.Clock);
        saving.Move(20);
        saving.Advance(0.9);
        File.WriteAllText(saving.Theme.LayoutPath, Saving.Moved(99));
        watcher.Check();

        saving.Advance(0.2);
        saving.Ui.RunPending();

        Assert.Equal(Saving.Moved(99), saving.OnDisk);
        saving.Advance(1);
        saving.Ui.RunPending();
        Assert.Equal(Saving.Moved(99), saving.Session.Document.Text);
        Assert.Equal(ExternalChangeWatcher.Label, saving.Session.History.NextUndo!.Label);
        Assert.Equal(2, saving.Session.History.Count);
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
        saving.Advance(5);
        Assert.Equal(Saving.Moved(99), saving.OnDisk);

        // The hand edit is a step of its own, so undoing it is the user's choice, and saves the text before it.
        saving.Session.Undo();
        saving.Advance(1);
        Assert.Equal(Saving.Moved(20), saving.OnDisk);
    }

    [Fact]
    public void A_hand_edit_saved_between_the_check_and_the_replace_is_put_back_and_reloaded()
    {
        using var saving = new Saving();
        using var watcher = new ExternalChangeWatcher(saving.Session, saving.File, saving.Ui, saving.Clock);
        saving.Move(20);
        saving.File.BeforeReplace = () =>
        {
            saving.File.BeforeReplace = null;
            File.WriteAllText(saving.Theme.LayoutPath, Saving.Moved(99));
        };

        saving.Advance(1);
        saving.Ui.RunPending();

        Assert.Equal(Saving.Moved(99), saving.OnDisk);
        saving.Advance(1);
        saving.Ui.RunPending();
        Assert.Equal(Saving.Moved(99), saving.Session.Document.Text);
        Assert.Equal(ExternalChangeWatcher.Label, saving.Session.History.NextUndo!.Label);
        Assert.Equal(Saving.Moved(99), saving.OnDisk);
        // The displaced file, kept for the check, is gone.
        Assert.Equal([Path.Combine(saving.Theme.Folder, "layout.yaml")],
            Directory.EnumerateFiles(saving.Theme.Folder, "*layout*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void A_write_that_lands_after_the_session_took_a_hand_edit_is_not_made()
    {
        // The autosave has taken its bytes when the watcher reads a hand edit and the UI thread accepts it.
        using var saving = new Saving();
        using var watcher = new ExternalChangeWatcher(saving.Session, saving.File, saving.Ui, saving.Clock);
        saving.Move(20);
        saving.Autosaver.BeforeWrite = () =>
        {
            saving.Autosaver.BeforeWrite = null;
            File.WriteAllText(saving.Theme.LayoutPath, Saving.Moved(99));
            watcher.Check();
            saving.Clock.Advance(Debounce);
            saving.Ui.RunPending();
            Assert.Equal(Saving.Moved(99), saving.Session.Document.Text);
        };

        saving.Advance(1);
        saving.Ui.RunPending();

        Assert.Equal(Saving.Moved(99), saving.OnDisk);
        Assert.Equal(Saving.Moved(99), saving.Session.Document.Text);
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
        Assert.Equal(0, saving.Clock.Armed);
        Assert.True(saving.Autosaver.Flush());
        Assert.Equal(Saving.Moved(99), saving.OnDisk);
    }

    [Fact]
    public void A_change_made_while_a_write_was_in_flight_is_saved_after_it()
    {
        using var saving = new Saving();
        saving.Move(20);
        // Undone while Moved(20) is being written: the undone text was the file's, so the change alone looks saved.
        saving.Autosaver.BeforeWrite = () =>
        {
            saving.Autosaver.BeforeWrite = null;
            saving.Session.Undo();
        };

        saving.Advance(1);

        Assert.Equal(Saving.Moved(20), saving.OnDisk);
        Assert.Equal(SaveStatus.Saving, saving.Autosaver.State.Status);
        saving.Advance(1);
        Assert.Equal(Saving.Layout, saving.OnDisk);
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
    }

    [Fact]
    public void A_file_changed_outside_is_not_written_and_is_handed_to_the_watcher()
    {
        using var saving = new Saving();
        var handedOver = 0;
        saving.File.ChangedOutside += (_, _) => handedOver++;
        saving.Move(20);
        File.WriteAllText(saving.Theme.LayoutPath, Saving.Moved(99));

        saving.Advance(1);

        Assert.Equal(1, handedOver);
        Assert.Equal(Saving.Moved(99), saving.OnDisk);
        Assert.Equal(SaveStatus.Saving, saving.Autosaver.State.Status);
        // Nothing takes it here, so the session's text waits and the file is looked at again, never written over.
        Assert.False(saving.Autosaver.Flush());
        saving.Advance(10);
        Assert.Equal(Saving.Moved(99), saving.OnDisk);
        Assert.True(handedOver > 1);
    }

    [Fact]
    public void Bytes_made_before_the_session_took_text_from_outside_are_stale()
    {
        using var saving = new Saving();
        var before = saving.File.Taken;
        var hand = System.Text.Encoding.UTF8.GetBytes(Saving.Moved(99));
        File.WriteAllBytes(saving.Theme.LayoutPath, hand);
        Assert.Equal(hand, saving.File.ReadChanged());

        saving.File.Accept(hand);

        Assert.Equal(before + 1, saving.File.Taken);
        Assert.True(saving.File.IsLast(hand));
        Assert.Null(saving.File.ReadChanged());
        var old = System.Text.Encoding.UTF8.GetBytes(Saving.Moved(20));
        Assert.Equal(LayoutWrite.Stale, saving.File.Write(old, before));
        Assert.Equal(Saving.Moved(99), saving.OnDisk);
        Assert.Equal(LayoutWrite.Written, saving.File.Write(old, saving.File.Taken));
        Assert.Equal(Saving.Moved(20), saving.OnDisk);
    }
}
