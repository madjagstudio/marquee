using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Validation;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// The toolbar's problems list: every error in the text as it is, not only the first, and the warnings of a theme that
/// loads, each with its line and the widget it is in, worked out again with each change.
public class EditorSessionProblemsTests
{
    const string Good =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: shape, shape: rect, at: [10, 10], size: [40, 40], style: { fill: \"#FF0000\" } }\n" +
        "  - type: group\n" +
        "    at: [100, 100]\n" +
        "    size: [200, 200]\n" +
        "    children:\n" +
        "      - { type: text, at: [0, 0], size: [100, 20], text: Hi }\n";

    static readonly string TwoErrors = Good
        .Replace("shape: rect", "shape: hexagon")
        .Replace("text: Hi }", "text: Hi, style: { size: -3 } }");

    static YamlPath P(string path) => YamlPath.Parse(path);

    [Fact]
    public void A_theme_that_loads_clean_has_no_problems()
    {
        using var folder = TempTheme.With(Good);
        using var session = folder.Open();

        Assert.Empty(session.Problems);
    }

    [Fact]
    public void Every_error_is_listed_with_its_line_and_widget()
    {
        using var folder = TempTheme.With(Good);
        using var session = folder.Open();

        session.Apply(new ReplaceDocument(TwoErrors, "Changed outside the editor"));

        Assert.Collection(session.Problems,
            problem =>
            {
                Assert.Equal((IssueSeverity.Error, 3, P("widgets[0]")), (problem.Severity, problem.Line, problem.Widget));
                Assert.Contains("hexagon", problem.Message);
            },
            problem =>
            {
                Assert.Equal((IssueSeverity.Error, 8, P("widgets[1].children[0]")), (problem.Severity, problem.Line, problem.Widget));
                Assert.Contains("size", problem.Message);
            });
        Assert.Equal(session.LoadError!.Reason, session.Problems[0].Message);
    }

    [Fact]
    public void A_theme_that_opens_with_errors_lists_them()
    {
        using var folder = TempTheme.With(TwoErrors);
        using var session = folder.Open();

        Assert.Equal(2, session.Problems.Count);
        Assert.All(session.Problems, problem => Assert.Equal(IssueSeverity.Error, problem.Severity));
    }

    [Fact]
    public void A_missing_image_is_a_warning_and_the_theme_still_loads()
    {
        using var folder = TempTheme.With(Good + "  - { type: image, at: [0, 300], size: [50, 50], src: images/missing.png }\n");
        using var session = folder.Open();

        Assert.Null(session.LoadError);
        var warning = Assert.Single(session.Problems);
        Assert.Equal(IssueSeverity.Warning, warning.Severity);
        Assert.Contains("missing.png", warning.Message);
    }

    [Fact]
    public void The_list_follows_each_change()
    {
        using var folder = TempTheme.With(Good);
        using var session = folder.Open();
        var changes = 0;
        session.Changed += (_, change) => changes += change.HasFlag(SessionChanges.Theme) ? 1 : 0;

        session.Apply(new ReplaceDocument(TwoErrors, "Changed outside the editor"));
        Assert.Equal(2, session.Problems.Count);

        Assert.True(session.Undo());
        Assert.Empty(session.Problems);
        Assert.Equal(2, changes);
    }
}
