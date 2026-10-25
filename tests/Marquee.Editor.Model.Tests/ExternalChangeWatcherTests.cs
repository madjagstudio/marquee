using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// Hand edits while the editor is open: layout.yaml changed by anything but the editor is reloaded as one undo step, the
/// editor's own saves are not, and a file that doesn't parse pauses editing with its error and line.
public class ExternalChangeWatcherTests
{
    static readonly TimeSpan Debounce = ExternalChangeWatcher.DefaultDebounce;

    sealed class Watching : IDisposable
    {
        public Watching(TempTheme? theme = null)
        {
            Saving = new Saving(theme);
            Watcher = new ExternalChangeWatcher(Saving.Session, Saving.File, Saving.Ui, Saving.Clock);
        }

        public Saving Saving { get; }

        public ExternalChangeWatcher Watcher { get; }

        public EditorSession Session => Saving.Session;

        /// Writes layout.yaml as another program would, then lets the watcher look and the UI thread run what it posts.
        public void HandEdit(string text)
        {
            File.WriteAllText(Saving.Theme.LayoutPath, text);
            Look();
        }

        /// How many things the watcher posted to the UI thread: none when it found nothing new.
        public int Look()
        {
            Saving.Ui.RunPending();
            Watcher.Check();
            Saving.Clock.Advance(Debounce);
            return Saving.Ui.RunPending();
        }

        public void Dispose()
        {
            Watcher.Dispose();
            Saving.Dispose();
        }
    }

    [Fact]
    public void A_hand_edit_is_reloaded_as_one_undo_step()
    {
        using var watching = new Watching();
        watching.Saving.Move(20);
        watching.Saving.Advance(1);

        watching.HandEdit(Saving.Moved(99));

        Assert.Equal(Saving.Moved(99), watching.Session.Document.Text);
        Assert.Equal(ExternalChangeWatcher.Label, watching.Session.History.NextUndo!.Label);
        Assert.Equal(2, watching.Session.History.Count);
        Assert.Equal(new SKRect(99, 10, 139, 50), watching.Session.Widgets[0].Bounds);

        // It is what the file says already: nothing to save.
        Assert.Equal(SaveState.Saved, watching.Saving.Autosaver.State);
        Assert.Equal(0, watching.Saving.Clock.Armed);

        watching.Session.Undo();
        Assert.Equal(Saving.Moved(20), watching.Session.Document.Text);
        watching.Saving.Advance(1);
        Assert.Equal(Saving.Moved(20), watching.Saving.OnDisk);
    }

    [Fact]
    public void The_editors_own_save_is_not_reloaded()
    {
        using var watching = new Watching();
        watching.Saving.Move(20);
        watching.Saving.Advance(1);
        Assert.Equal(Saving.Moved(20), watching.Saving.OnDisk);

        Assert.Equal(0, watching.Look());

        Assert.Equal(1, watching.Session.History.Count);
    }

    [Fact]
    public void The_file_as_the_editor_read_it_is_not_reloaded()
    {
        using var watching = new Watching();

        Assert.Equal(0, watching.Look());

        Assert.False(watching.Session.CanUndo);
    }

    [Fact]
    public void A_hand_edit_that_does_not_load_pauses_editing_with_its_line()
    {
        using var watching = new Watching();

        watching.HandEdit(Saving.Layout.Replace("shape: rect", "shape: hexagon"));

        Assert.True(watching.Session.IsPaused);
        Assert.Equal(5, watching.Session.LoadError!.Line);

        watching.HandEdit(Saving.Moved(40));
        Assert.False(watching.Session.IsPaused);
        Assert.Equal(Saving.Moved(40), watching.Session.Document.Text);
    }

    [Fact]
    public void A_hand_edit_that_is_not_yaml_pauses_editing_with_its_line_and_is_never_saved_over()
    {
        using var watching = new Watching();
        watching.Saving.Move(20);
        watching.Saving.Advance(1);
        const string notYaml = "canvas: { orientation: landscape }\nwidgets: [\n";

        watching.HandEdit(notYaml);

        Assert.True(watching.Session.IsPaused);
        Assert.True(watching.Session.IsFileUnreadable);
        Assert.Equal(3, watching.Session.LoadError!.Line);
        Assert.Equal(Saving.Moved(20), watching.Session.Document.Text);
        Assert.Throws<InvalidOperationException>(() => watching.Saving.Move(30));
        watching.Saving.Advance(10);
        Assert.Equal(notYaml, watching.Saving.OnDisk);

        // Fixed by hand: editing goes on from the file.
        watching.HandEdit(Saving.Moved(50));
        Assert.False(watching.Session.IsPaused);
        Assert.Equal(Saving.Moved(50), watching.Session.Document.Text);
    }

    [Fact]
    public void A_file_that_is_not_yaml_is_left_by_reverting()
    {
        using var watching = new Watching();
        watching.HandEdit("widgets: [\n");
        Assert.True(watching.Session.IsFileUnreadable);

        // The opened text is the session's text already, but reverting writes it over the file.
        watching.Session.Apply(watching.Saving.Backup.Revert());

        Assert.False(watching.Session.IsPaused);
        watching.Saving.Advance(1);
        Assert.Equal(Saving.Layout, watching.Saving.OnDisk);
        Assert.Equal(0, watching.Look());
    }

    [Fact]
    public void A_file_that_is_not_yaml_fixed_back_to_the_sessions_text_ends_the_pause()
    {
        using var watching = new Watching();
        watching.HandEdit("widgets: [\n");

        watching.HandEdit(Saving.Layout);

        Assert.False(watching.Session.IsPaused);
        Assert.False(watching.Session.CanUndo);
    }

    [Fact]
    public void Undo_while_the_file_is_not_yaml_is_refused_and_the_hand_edit_is_never_saved_over()
    {
        using var watching = new Watching();
        watching.Saving.Move(20);
        watching.Saving.Advance(1);
        var broken = Saving.Moved(20).Replace("widgets:", "widgets: [");

        watching.HandEdit(broken);
        Assert.True(watching.Session.IsFileUnreadable);

        Assert.False(watching.Session.CanUndo);
        Assert.False(watching.Session.Undo());
        Assert.Equal(Saving.Moved(20), watching.Session.Document.Text);
        watching.Saving.Advance(1.5);
        watching.Saving.Ui.RunPending();
        Assert.Equal(broken, watching.Saving.OnDisk);

        // Once the file parses again, the history is there to undo as before.
        watching.HandEdit(Saving.Moved(30));
        Assert.True(watching.Session.CanUndo);
        Assert.True(watching.Session.Undo());
        Assert.Equal(Saving.Moved(20), watching.Session.Document.Text);
    }

    [Fact]
    public void Undo_while_a_hand_edit_is_not_a_theme_is_refused_and_the_hand_edit_is_never_saved_over()
    {
        // YAML that isn't a valid theme used to take Ctrl+Z: the step before it was loaded and autosaved over the file.
        using var watching = new Watching();
        watching.Saving.Move(20);
        watching.Saving.Advance(1);
        var broken = Saving.Moved(20).Replace("shape: rect", "shape: hexagon");

        watching.HandEdit(broken);
        Assert.Equal(PauseReason.Theme, watching.Session.Pause);

        Assert.False(watching.Session.CanUndo);
        Assert.False(watching.Session.Undo());
        Assert.Equal(broken, watching.Session.Document.Text);
        watching.Saving.Advance(1.5);
        watching.Saving.Ui.RunPending();
        Assert.Equal(broken, watching.Saving.OnDisk);

        // "Revert to opened version" is the explicit way out, and it is a step that can be undone.
        watching.Session.Apply(watching.Saving.Backup.Revert());
        Assert.Equal(PauseReason.None, watching.Session.Pause);
        watching.Saving.Advance(1);
        Assert.Equal(Saving.Layout, watching.Saving.OnDisk);
        Assert.True(watching.Session.CanUndo);
    }

    [Fact]
    public void Redo_while_the_file_is_not_yaml_is_refused_and_reverting_is_the_way_out()
    {
        using var watching = new Watching();
        watching.Saving.Move(20);
        watching.Session.Undo();
        Assert.True(watching.Session.CanRedo);
        const string notYaml = "widgets: [\n";

        watching.HandEdit(notYaml);

        Assert.False(watching.Session.CanRedo);
        Assert.False(watching.Session.Redo());
        Assert.Equal(Saving.Layout, watching.Session.Document.Text);
        watching.Saving.Advance(2);
        Assert.Equal(notYaml, watching.Saving.OnDisk);

        // "Revert to opened version" is the explicit way out: it writes the opened text over the file.
        watching.Session.Apply(watching.Saving.Backup.Revert());
        Assert.Equal(PauseReason.None, watching.Session.Pause);
        Assert.True(watching.Session.CanRedo);
        watching.Saving.Advance(1);
        Assert.Equal(Saving.Layout, watching.Saving.OnDisk);
    }

    [Fact]
    public void A_theme_error_stays_paused_when_a_file_that_is_not_yaml_is_put_back_as_the_sessions_text()
    {
        using var watching = new Watching();
        var broken = Saving.Layout.Replace("shape: rect", "shape: hexagon");
        watching.HandEdit(broken);
        Assert.Equal(PauseReason.Theme, watching.Session.Pause);
        var error = watching.Session.LoadError;

        watching.HandEdit("widgets: [\n");
        Assert.Equal(PauseReason.File, watching.Session.Pause);
        Assert.True(watching.Session.IsFileUnreadable);

        watching.HandEdit(broken);

        Assert.Equal(PauseReason.Theme, watching.Session.Pause);
        Assert.False(watching.Session.IsFileUnreadable);
        Assert.Equal(error, watching.Session.LoadError);
        Assert.Equal(1, watching.Session.History.Count);
    }

    [Fact]
    public void The_pause_reason_follows_each_text_from_outside()
    {
        using var watching = new Watching();
        Assert.Equal(PauseReason.None, watching.Session.Pause);

        watching.HandEdit("widgets: [\n");
        Assert.Equal(PauseReason.File, watching.Session.Pause);
        watching.HandEdit(Saving.Layout.Replace("shape: rect", "shape: hexagon"));
        Assert.Equal(PauseReason.Theme, watching.Session.Pause);
        watching.HandEdit(Saving.Moved(40));
        Assert.Equal(PauseReason.None, watching.Session.Pause);
        Assert.Null(watching.Session.LoadError);

        // Unreadable again, then put back as the session's text: no theme error from before comes back.
        watching.HandEdit("widgets: [\n");
        watching.HandEdit(Saving.Moved(40));
        Assert.Equal(PauseReason.None, watching.Session.Pause);
        Assert.Null(watching.Session.LoadError);
        Assert.False(watching.Session.IsPaused);
    }

    [Fact]
    public void A_file_still_being_written_is_read_again_shortly()
    {
        using var watching = new Watching();
        File.WriteAllText(watching.Saving.Theme.LayoutPath, Saving.Moved(60));

        using (new FileStream(watching.Saving.Theme.LayoutPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(0, watching.Look());
            Assert.Equal(1, watching.Saving.Clock.Armed);
        }

        watching.Saving.Clock.Advance(Debounce);
        watching.Saving.Ui.RunPending();

        Assert.Equal(Saving.Moved(60), watching.Session.Document.Text);
    }

    [Fact]
    public void A_hand_edit_during_a_drag_waits_for_the_drag_to_end()
    {
        using var watching = new Watching();
        watching.Session.BeginPreview();
        watching.Session.PreviewMove(Saving.X.Parent!.Parent!, new SKPoint(200, 10));

        watching.HandEdit(Saving.Moved(70));
        Assert.True(watching.Session.IsPreviewing);
        Assert.Equal(Saving.Layout, watching.Session.Document.Text);

        watching.Session.CancelPreview();
        watching.Saving.Ui.RunPending();

        Assert.Equal(Saving.Moved(70), watching.Session.Document.Text);
    }

    [Fact]
    public void A_hand_edit_during_a_panel_drag_waits_for_it_to_end_and_is_a_step_of_its_own()
    {
        using var watching = new Watching();
        watching.Session.BeginInteraction();
        watching.Session.Apply(new SetValue(Saving.X, "20") { MergeKey = "slide" });

        watching.HandEdit(Saving.Moved(70));
        Assert.Equal(Saving.Moved(20), watching.Session.Document.Text);

        watching.Session.EndInteraction();
        watching.Saving.Ui.RunPending();

        Assert.Equal(Saving.Moved(70), watching.Session.Document.Text);
        Assert.Equal(2, watching.Session.History.Count);
        watching.Saving.Advance(5);
        Assert.Equal(Saving.Moved(70), watching.Saving.OnDisk);
    }

    [Fact]
    public void The_folder_is_watched()
    {
        using var watching = new Watching();
        watching.Watcher.Start();
        // Start looks once, for a change made while the theme was opening.
        watching.Saving.Clock.Advance(Debounce);
        Assert.Equal(0, watching.Saving.Ui.RunPending());

        File.WriteAllText(watching.Saving.Theme.LayoutPath, Saving.Moved(80));

        Assert.True(watching.Saving.Clock.WaitUntilArmed(TimeSpan.FromSeconds(10)));
        watching.Saving.Clock.Advance(Debounce);
        watching.Saving.Ui.RunPending();
        Assert.Equal(Saving.Moved(80), watching.Session.Document.Text);
    }
}
