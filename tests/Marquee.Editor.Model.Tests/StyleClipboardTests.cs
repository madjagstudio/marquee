using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Copy takes a widget's effective style, its named style flattened with its overrides; paste writes the keys each
/// target's type draws with as overrides in place, in one undo step.
public class StyleClipboardTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "styles:\n" +
        "  base: { size: 20, stroke: { width: 2, color: \"#FFFFFF20\", cap: round } }\n" +
        "  label: { extends: base, color: \"#8B95AA\", weight: bold }\n" +
        "widgets:\n" +
        "  - { type: text, at: [10, 10], size: [200, 40], text: A, style: { extends: label, color: \"#FF0000\", fill: \"#000000\" } }\n" +
        "  - { type: text, at: [10, 60], size: [200, 40], text: B }\n" +
        "  - { type: bar, at: [10, 110], size: [200, 20], source: cpu.load, style: label }\n" +
        "  - { type: graph, at: [10, 140], size: [200, 60], source: cpu.load }\n" +
        "  - { type: text, at: [10, 210], size: [200, 40], text: C, style: label }\n";

    static WidgetRef W(EditorSession session, int index) => session.Widget(YamlPath.Parse($"widgets[{index}]"))!;

    [Fact]
    public void Copy_takes_the_named_style_with_its_overrides_flattened()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var clipboard = new StyleClipboard();

        clipboard.Copy(session.Document, W(session, 0));

        Assert.Equal("{ size: 20, stroke: { width: 2, color: \"#FFFFFF20\", cap: round }, color: \"#FF0000\", weight: bold, fill: \"#000000\" }",
            YamlWriter.Flow(clipboard.Style!));
    }

    [Fact]
    public void Paste_writes_the_keys_that_apply_as_overrides_in_one_step()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var clipboard = new StyleClipboard();
        clipboard.Copy(session.Document, W(session, 0));

        session.Apply(clipboard.Paste([W(session, 1), W(session, 2), W(session, 3)]));

        var text = session.Document.Text;
        // A text widget takes all of it, with no named style under it.
        Assert.Contains("text: B, style: { size: 20, stroke: { width: 2, color: \"#FFFFFF20\" }, color: \"#FF0000\", weight: bold, fill: \"#000000\" } }", text);
        // A bar draws no text and no stroke cap, and what its named style already gives the same isn't written again.
        Assert.Contains("source: cpu.load, style: { extends: label, color: \"#FF0000\", fill: \"#000000\" } }", text);
        // A graph draws its stroke's cap too, but no text.
        Assert.Contains("source: cpu.load, style: { stroke: { width: 2, color: \"#FFFFFF20\", cap: round }, color: \"#FF0000\", fill: \"#000000\" } }", text);
        Assert.Equal("Paste style", session.History.NextUndo!.Label);

        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Pasting_a_widgets_own_style_back_changes_nothing()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var clipboard = new StyleClipboard();
        session.Select([YamlPath.Parse("widgets[4]")]);

        Assert.True(clipboard.Copy(session));
        Assert.False(clipboard.Paste(session));
        Assert.Equal(Layout, session.Document.Text);
    }

    [Fact]
    public void Paste_keeps_what_the_target_sets_that_the_copy_doesnt()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var clipboard = new StyleClipboard();
        clipboard.Copy(session.Document, W(session, 4));

        session.Select([YamlPath.Parse("widgets[0]")]);
        Assert.True(clipboard.Paste(session));

        // The copy's color is label's grey; the target's own fill stays.
        Assert.Contains("style: { extends: label, color: \"#8B95AA\", fill: \"#000000\" } }", session.Document.Text);
    }

    [Fact]
    public void Nothing_copied_or_selected_pastes_nothing()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var clipboard = new StyleClipboard();

        Assert.False(clipboard.Copy(session));
        Assert.False(clipboard.Paste(session));
        Assert.Throws<InvalidOperationException>(() => clipboard.Paste([W(session, 0)]));
    }

    [Theory]
    [MemberData(nameof(PropertyCatalogTests.Examples), MemberType = typeof(PropertyCatalogTests))]
    public void Every_examples_styles_paste_onto_every_type(string example)
    {
        using var folder = TempTheme.Example(example);
        using var session = folder.Open();
        var widgets = session.Widgets.SelectMany(w => w.Children.Prepend(w)).ToList();
        var targets = widgets.GroupBy(w => w.Type).Select(g => g.First()).ToList();
        var clipboard = new StyleClipboard();

        foreach (var source in widgets.GroupBy(w => w.Type).Select(g => g.First()))
        {
            clipboard.Copy(session.Document, source);
            var before = session.Document.Text;
            session.Apply(clipboard.Paste(targets));
            Assert.Null(session.LoadError);
            if (session.Document.Text != before) Assert.True(session.Undo());
            Assert.Equal(before, session.Document.Text);
        }
    }
}
