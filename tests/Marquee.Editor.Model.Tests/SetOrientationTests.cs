using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// Switching orientation can turn a row into a column without resizing widgets, in one undo step.
public class SetOrientationTests
{
    const string Row =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: text, at: [100, 200], size: [200, 40], text: a } # left\n" +
        "  - { type: text, at: [900, 200], size: [200, 40], text: b }\n";

    [Fact]
    public void Rearranging_mirrors_each_centre_across_the_diagonal_and_keeps_sizes()
    {
        using var theme = TempTheme.With(Row);
        using var session = EditorSession.Open(theme.Folder);

        session.Apply(new SetOrientation("portrait", Rearrange: true));

        // Centres (200, 220) and (1000, 220) become (220, 200) and (220, 1000).
        Assert.Contains("orientation: portrait", session.Document.Text);
        Assert.Contains("at: [120, 180], size: [200, 40], text: a } # left", session.Document.Text);
        Assert.Contains("at: [120, 980], size: [200, 40], text: b }", session.Document.Text);
        session.Undo();
        Assert.Equal(Row, session.Document.Text);
    }

    [Fact]
    public void Without_rearranging_only_the_setting_changes()
    {
        using var theme = TempTheme.With(Row);
        using var session = EditorSession.Open(theme.Folder);

        session.Apply(new SetOrientation("portrait", Rearrange: false));

        Assert.Equal(Row.Replace("landscape", "portrait"), session.Document.Text);
    }

    [Fact]
    public void A_group_moves_as_a_whole_and_its_children_stay_relative()
    {
        using var theme = TempTheme.With(
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - type: group\n    at: [400, 100]\n    size: [100, 50]\n    children:\n      - { type: text, at: [0, 0], size: [100, 50], text: x }\n");
        using var session = EditorSession.Open(theme.Folder);

        session.Apply(new SetOrientation("portrait", Rearrange: true));

        Assert.Contains("at: [0, 0], size: [100, 50]", session.Document.Text);
        Assert.Contains("at: [75, 425]\n    size: [100, 50]", session.Document.Text);
    }

    [Fact]
    public void Widgets_left_partly_off_the_canvas_are_counted()
    {
        using var theme = TempTheme.With(
            "canvas: { orientation: landscape }\nwidgets:\n  - { type: text, at: [100, 100], size: [1200, 40], text: wide }\n");
        using var session = EditorSession.Open(theme.Folder);

        session.Apply(new SetOrientation("portrait", Rearrange: true));

        Assert.Equal(1, SetOrientation.PartlyOffCanvas(session));
    }

    [Fact]
    public void Rearranging_preserves_rotation_and_other_keys()
    {
        const string layout = "canvas: { orientation: landscape }\nwidgets:\n" +
            "  - { type: text, at: [0, 0], size: [200, 40], rotate: 30, text: x } # rotated\n";
        using var theme = TempTheme.With(layout);
        using var session = theme.Open();

        session.Apply(new SetOrientation("portrait", Rearrange: true));

        Assert.Contains("at: [-80, 80]", session.Document.Text);
        Assert.Contains("size: [200, 40], rotate: 30, text: x", session.Document.Text);
        Assert.Contains("# rotated", session.Document.Text);
        Assert.Equal(20, session.Widgets[0].Bounds.MidX);
        Assert.Equal(100, session.Widgets[0].Bounds.MidY);
        session.Undo();
        Assert.Equal(layout, session.Document.Text);
    }

    [Fact]
    public void Rearranging_to_landscape_rounds_moved_positions_to_whole_pixels()
    {
        using var theme = TempTheme.With("canvas: { orientation: portrait }\nwidgets:\n" +
            "  - { type: text, at: [100.2, 200.8], size: [200, 40], text: x }\n");
        using var session = theme.Open();

        session.Apply(new SetOrientation("landscape", Rearrange: true));

        Assert.Contains("orientation: landscape", session.Document.Text);
        Assert.Contains("at: [121, 180], size: [200, 40]", session.Document.Text);
        Assert.Equal("Change orientation", new SetOrientation("landscape", true).Label);
    }

    [Fact]
    public void Off_canvas_count_includes_only_roots_and_accepts_boxes_on_the_edge()
    {
        using var theme = TempTheme.With("canvas: { orientation: portrait }\nwidgets:\n" +
            "  - { type: text, at: [0, 0], size: [480, 1920], text: edge }\n" +
            "  - { type: text, at: [-1, 0], size: [10, 10], text: left }\n" +
            "  - { type: text, at: [10, 1911], size: [10, 10], text: bottom }\n" +
            "  - type: group\n    at: [0, 0]\n    size: [100, 100]\n    children:\n" +
            "      - { type: text, at: [500, 0], size: [100, 50], text: child }\n");
        using var session = theme.Open();

        Assert.Equal(2, SetOrientation.PartlyOffCanvas(session));
    }
}
