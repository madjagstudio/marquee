using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Undo in the session: a slider's run of changes is one step, at most 500 steps are kept, and a refused or paused
/// session doesn't take commands.
public class EditorSessionUndoTests
{
    const string Layout = "canvas: { orientation: landscape }\nwidgets:\n  - { type: text, at: [10, 10], size: [200, 40], text: A }\n";

    static readonly YamlPath X = YamlPath.Parse("widgets[0].at[0]");

    static SetValue Slide(int x) => new(X, x.ToString(System.Globalization.CultureInfo.InvariantCulture)) { MergeKey = X };

    [Fact]
    public void A_sliders_changes_inside_the_window_are_one_step()
    {
        var time = new ManualTime();
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open(new EditorSessionOptions { Time = time });

        for (var x = 11; x <= 20; x++)
        {
            session.Apply(Slide(x));
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(1, session.History.Count);
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void A_pause_longer_than_the_window_starts_a_new_step()
    {
        var time = new ManualTime();
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open(new EditorSessionOptions { Time = time, MergeWindow = TimeSpan.FromMilliseconds(500) });

        session.Apply(Slide(11));
        time.Advance(TimeSpan.FromMilliseconds(400));
        session.Apply(Slide(12));
        time.Advance(TimeSpan.FromMilliseconds(600));
        session.Apply(Slide(13));

        Assert.Equal(2, session.History.Count);
        session.Undo();
        Assert.Equal(Layout.Replace("[10, 10]", "[12, 10]"), session.Document.Text);
    }

    [Fact]
    public void The_session_keeps_the_last_500_steps()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        for (var x = 11; x <= 512; x++)
            session.Apply(new SetValue(X, x.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var undone = 0;
        while (session.Undo()) undone++;
        Assert.Equal(500, undone);
        Assert.Equal(Layout.Replace("[10, 10]", "[12, 10]"), session.Document.Text);
    }

    [Fact]
    public void Undo_and_redo_with_nothing_to_do_return_false()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        Assert.False(session.Undo());
        Assert.False(session.Redo());
    }

    [Fact]
    public void A_disposed_session_takes_no_commands()
    {
        using var folder = TempTheme.With(Layout);
        var session = folder.Open();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.Apply(Slide(11)));
        Assert.Throws<ObjectDisposedException>(() => session.RenderFrame(TimeSpan.Zero));
    }
}
