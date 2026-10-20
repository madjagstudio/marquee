using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// Text from outside the editor (a hand edit, a revert) that doesn't load as a theme: the canvas stays on the last good
/// theme, the error is shown with its line, and editing pauses until the text loads again or the change is undone.
public class EditorSessionLoadErrorTests
{
    const string Good =
        "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#000000\"\n" +
        "widgets:\n  - { type: shape, shape: rect, at: [10, 10], size: [40, 40], style: { fill: \"#FF0000\" } }\n";

    const string Broken =
        "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#000000\"\n" +
        "widgets:\n  - { type: shape, shape: hexagon, at: [10, 10], size: [40, 40], style: { fill: \"#00FF00\" } }\n";

    static ReplaceDocument HandEdit(string text) => new(text, "Changed outside the editor");

    [Fact]
    public void Text_that_does_not_load_keeps_the_last_good_theme_and_shows_the_error_with_its_line()
    {
        using var folder = TempTheme.With(Good);
        using var session = folder.Open();
        var theme = session.Theme;
        var widgets = session.Widgets;

        session.Apply(HandEdit(Broken));

        Assert.Equal(Broken, session.Document.Text);
        Assert.Same(theme, session.Theme);
        Assert.False(theme!.IsDisposed);
        Assert.Same(widgets, session.Widgets);
        Assert.Equal(5, session.LoadError!.Line);
        Assert.Contains("hexagon", session.LoadError.Reason);
        Assert.True(session.IsPaused);
        Frames.AssertNear(SKColors.Red, Frames.PixelAt(session, 30, 30));
    }

    [Fact]
    public void While_paused_only_text_from_outside_and_undo_are_taken()
    {
        using var folder = TempTheme.With(Good);
        using var session = folder.Open();
        session.Apply(HandEdit(Broken));

        Assert.Throws<InvalidOperationException>(() => session.Apply(new SetValue(YamlPath.Parse("widgets[0].at[0]"), "20")));
        Assert.Throws<InvalidOperationException>(() => session.BeginPreview());
        Assert.Equal(Broken, session.Document.Text);

        Assert.True(session.Undo());
        Assert.False(session.IsPaused);
        Assert.Null(session.LoadError);
        Assert.Equal(Good, session.Document.Text);

        Assert.True(session.Redo());
        Assert.True(session.IsPaused);
    }

    [Fact]
    public void Text_that_loads_again_ends_the_pause_with_a_new_theme()
    {
        using var folder = TempTheme.With(Good);
        using var session = folder.Open();
        var theme = session.Theme!;
        session.Apply(HandEdit(Broken));

        session.Apply(HandEdit(Broken.Replace("hexagon", "rect")));

        Assert.False(session.IsPaused);
        Assert.True(theme.IsDisposed);
        Frames.AssertNear(SKColors.Lime, Frames.PixelAt(session, 30, 30));
    }

    [Theory]
    [InlineData("widgets:\n  - :\n")]
    [InlineData("- :")]
    public void A_key_with_no_name_is_refused_as_text_that_is_not_yaml(string text)
    {
        // YamlDotNet throws ArgumentException for these, which LayoutDocument and LayoutParser both turn into their own
        // errors; LayoutDocument reads the text first, so it never reaches the theme loader.
        using var folder = TempTheme.With(Good);
        using var session = folder.Open();

        Assert.Contains("YAML syntax error", Assert.Throws<LayoutEditException>(() => session.Apply(HandEdit(text))).Message);
        Assert.Equal(Good, session.Document.Text);
        Assert.False(session.IsPaused);

        File.WriteAllText(folder.LayoutPath, text);
        Assert.Throws<LayoutEditException>(() => EditorSession.Open(folder.Folder));
    }

    [Fact]
    public void A_load_that_fails_otherwise_leaves_the_step_to_undo_or_redo_again()
    {
        using var folder = TempTheme.With(Good);
        using var session = folder.Open();
        session.Apply(new SetValue(YamlPath.Parse("widgets[0].at[0]"), "20"));
        var edited = session.Document.Text;
        var theme = session.Theme;
        session.LoadHook = _ => throw new IOException("The disk went away.");

        Assert.Throws<IOException>(() => session.Undo());
        Assert.Equal(edited, session.Document.Text);
        Assert.Same(theme, session.Theme);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);

        Assert.Throws<IOException>(() => session.Apply(new SetValue(YamlPath.Parse("widgets[0].at[0]"), "30")));
        Assert.Equal(edited, session.Document.Text);
        Assert.Equal(1, session.History.Count);

        session.LoadHook = null;
        Assert.True(session.Undo());
        Assert.Equal(Good, session.Document.Text);
        session.LoadHook = _ => throw new IOException("The disk went away.");

        Assert.Throws<IOException>(() => session.Redo());
        Assert.Equal(Good, session.Document.Text);
        Assert.True(session.CanRedo);
        Assert.False(session.CanUndo);

        session.LoadHook = null;
        Assert.True(session.Redo());
        Assert.Equal(edited, session.Document.Text);
    }

    [Fact]
    public void Text_that_is_not_yaml_is_refused_and_changes_nothing()
    {
        using var folder = TempTheme.With(Good);
        using var session = folder.Open();

        Assert.Throws<LayoutEditException>(() => session.Apply(HandEdit("widgets: [\n")));

        Assert.Equal(Good, session.Document.Text);
        Assert.False(session.IsPaused);
        Assert.False(session.CanUndo);
    }
}
