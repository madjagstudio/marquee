using System.Globalization;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// A drag in a panel (a slider, the colour picker, a gradient's stops or dial) is one interaction: each move changes the
/// text, so the canvas shows it at once, but the whole drag is one undo step however long it rests, and it is saved, and
/// kept for crash recovery, once, after it is let go.
public class EditorSessionInteractionTests
{
    static SetValue Slide(int x) => new(Saving.X, x.ToString(CultureInfo.InvariantCulture)) { MergeKey = "slide" };

    [Fact]
    public void A_long_resting_slide_is_one_undo_step_and_one_file_write_after_release()
    {
        using var saving = new Saving();
        using var recovery = new RecoveryStore(saving.Session, saving.Folders, saving.Ui, saving.Clock);
        var writes = 0;
        saving.Autosaver.BeforeWrite = () => writes++;

        saving.Session.BeginInteraction();
        foreach (var x in new[] { 20, 30, 40 })
        {
            saving.Session.Apply(Slide(x));
            // A rest longer than the undo merge window, autosave's delay and recovery's.
            saving.Advance(3);
            saving.Ui.RunPending();
        }

        Assert.Equal(Saving.Moved(40), saving.Session.Document.Text);
        Assert.Equal(Saving.Layout, saving.OnDisk);
        Assert.Equal(0, writes);
        Assert.False(File.Exists(recovery.Path));
        Assert.Equal(SaveStatus.Saving, saving.Autosaver.State.Status);

        saving.Session.EndInteraction();
        saving.Advance(1);
        saving.Ui.RunPending();

        Assert.Equal(Saving.Moved(40), saving.OnDisk);
        Assert.Equal(1, writes);
        Assert.Equal(Saving.Moved(40), File.ReadAllText(recovery.Path));
        Assert.Equal(SaveState.Saved, saving.Autosaver.State);
        Assert.Equal(1, saving.Session.History.Count);
        saving.Advance(10);
        Assert.Equal(1, writes);

        Assert.True(saving.Session.Undo());
        Assert.Equal(Saving.Layout, saving.Session.Document.Text);
    }

    [Fact]
    public void A_change_waiting_to_be_saved_when_a_drag_begins_is_saved_with_it_after_release()
    {
        using var saving = new Saving();
        var writes = 0;
        saving.Autosaver.BeforeWrite = () => writes++;
        saving.Move(15);
        saving.Advance(0.5);

        saving.Session.BeginInteraction();
        saving.Session.Apply(Slide(20));
        saving.Advance(5);
        Assert.Equal(0, writes);
        Assert.Equal(Saving.Layout, saving.OnDisk);

        saving.Session.EndInteraction();
        saving.Advance(1);
        Assert.Equal(1, writes);
        Assert.Equal(Saving.Moved(20), saving.OnDisk);
        Assert.Equal(2, saving.Session.History.Count);
    }

    [Fact]
    public void Each_drag_is_a_step_of_its_own_and_one_back_where_it_began_is_none()
    {
        using var saving = new Saving();

        saving.Session.BeginInteraction();
        saving.Session.Apply(Slide(20));
        saving.Session.EndInteraction();
        saving.Session.BeginInteraction();
        saving.Session.Apply(Slide(30));
        saving.Session.EndInteraction();
        Assert.Equal(2, saving.Session.History.Count);

        // There and back: no step, and the drag's next move doesn't join the drag before it.
        saving.Session.BeginInteraction();
        saving.Session.Apply(Slide(40));
        saving.Session.Apply(Slide(30));
        saving.Advance(2);
        saving.Session.Apply(Slide(50));
        saving.Session.EndInteraction();
        Assert.Equal(3, saving.Session.History.Count);
        Assert.Equal(Saving.Moved(30), saving.Session.History.NextUndo!.Before);
    }

    [Fact]
    public void Undo_redo_another_drag_and_a_canvas_drag_wait_for_it_to_end()
    {
        using var saving = new Saving();
        saving.Move(20);

        saving.Session.BeginInteraction();
        Assert.True(saving.Session.IsInteracting);
        Assert.False(saving.Session.CanUndo);
        Assert.False(saving.Session.Undo());
        Assert.Throws<InvalidOperationException>(() => saving.Session.BeginInteraction());
        Assert.Throws<InvalidOperationException>(() => saving.Session.BeginPreview());

        saving.Session.EndInteraction();
        saving.Session.EndInteraction();
        Assert.False(saving.Session.IsInteracting);
        Assert.True(saving.Session.CanUndo);

        saving.Session.BeginPreview();
        Assert.Throws<InvalidOperationException>(() => saving.Session.BeginInteraction());
        saving.Session.CancelPreview();
    }

    [Fact]
    public void A_close_during_a_drag_saves_what_it_made()
    {
        using var saving = new Saving();
        saving.Session.BeginInteraction();
        saving.Session.Apply(Slide(20));

        Assert.True(saving.Autosaver.Flush());

        Assert.False(saving.Session.IsInteracting);
        Assert.Equal(Saving.Moved(20), saving.OnDisk);
    }
}
