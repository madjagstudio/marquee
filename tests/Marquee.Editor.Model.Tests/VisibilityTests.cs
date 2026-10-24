using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

public class VisibilityTests
{
    static readonly YamlPath Widget = YamlPath.Parse("widgets[0]");
    const string Text = "{ type: text, at: [10, 10], size: [80, 30], text: Hello }";
    const string Prefix = "canvas: { orientation: landscape }\n";

    [Theory]
    [InlineData("1", "This text is 1px, too small to see")]
    [InlineData("5.5", "This text is 5.5px, too small to see")]
    public void Small_text_explains_its_size(string size, string expected) =>
        Check(Text.Replace("text: Hello", $"text: Hello, style: {{ size: {size} }}"), expected);

    [Theory]
    [InlineData("style: { size: 6 }")]
    [InlineData("style: { opacity: 0.05 }")]
    [InlineData("style: { size: 24, opacity: 1 }")]
    [InlineData("locked: true")]
    public void Visible_widgets_have_no_warning(string settings) => Check(Text.Replace("text: Hello", $"text: Hello, {settings}"));

    [Fact]
    public void Normal_widget_has_no_warning() => Check(Text);

    [Theory]
    [InlineData("0")]
    [InlineData("0.049")]
    public void Transparent_widget_reports_why(string opacity) =>
        Check(Text.Replace("text: Hello", $"text: Hello, style: {{ opacity: {opacity} }}"), "This is fully transparent");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hidden_widget_or_ancestor_reports_layers(bool ancestor)
    {
        var item = ancestor ? Text : Text.Replace("text: Hello", "text: Hello, hidden: true");
        Check(Group(item, ancestor ? "hidden: true," : ""), ["This is hidden. Show it in Layers."], Widget.Child("children").Item(0));
    }

    [Fact]
    public void Named_styles_and_all_group_opacities_contribute()
    {
        using var folder = TempTheme.With(Prefix + "styles: { faint: { opacity: 0.2 }, inherited: { extends: faint } }\nwidgets: [" +
            Group(Group(Text.Replace("text: Hello", "text: Hello, style: inherited"), "style: { opacity: 0.5 },"), "style: { opacity: 0.4 },").Replace("at: [100, 100]", "at: [0, 0]") + "]");
        using var session = folder.Open();
        Assert.Null(session.LoadError);
        Assert.Equal(["This is fully transparent"], Visibility.Problems(session, Widget.Child("children").Item(0).Child("children").Item(0)));
    }

    [Fact]
    public void Text_size_inherits_from_a_group_but_can_be_overridden()
    {
        Check(Group(Text, "style: { size: 1 },"), ["This text is 1px, too small to see"], Widget.Child("children").Item(0));
        Check(Group(Text.Replace("text: Hello", "text: Hello, style: { size: 24 }"), "style: { size: 1 },"), [], Widget.Child("children").Item(0));
    }

    [Theory]
    [InlineData("[200, 10]", "This is outside its group's box, so it's hidden")]
    [InlineData("[90, 10]", "This is partly outside its group's box, so it's partly hidden")]
    public void Group_clipping_reports_full_or_partial(string at, string expected) =>
        Check(Group(Text.Replace("[10, 10]", at)), [expected], Widget.Child("children").Item(0));

    [Theory]
    [InlineData("[3000, 10]", "This is outside the screen")]
    [InlineData("[-40, 10]", "This is partly outside the screen")]
    public void Screen_clipping_reports_full_or_partial(string at, string expected) => Check(Text.Replace("[10, 10]", at), expected);

    [Fact]
    public void Clipping_checks_outer_groups_too() =>
        Check(Group(Group(Text.Replace("[10, 10]", "[110, 10]"), "", 300)),
            ["This is outside its group's box, so it's hidden"], Widget.Child("children").Item(0).Child("children").Item(0));

    [Fact]
    public void Rotated_parent_does_not_make_a_contained_child_look_clipped() =>
        Check(Group(Text, "rotate: 45," ).Replace("at: [100, 100]", "at: [300, 300]"), [], Widget.Child("children").Item(0));

    [Fact]
    public void A_turned_child_can_be_partly_clipped() =>
        Check(Group(Text.Replace("text: Hello", "text: Hello, rotate: 90")),
            ["This is partly outside its group's box, so it's partly hidden"], Widget.Child("children").Item(0));

    [Fact]
    public void Owners_text_below_396_by_53_group_is_explained_until_move_fits_it()
    {
        using var folder = TempTheme.With(Prefix + "widgets: [" + Group(Text.Replace("[10, 10]", "[10, 70]"))
            .Replace("size: [100, 100]", "size: [396, 53]") + "]");
        using var session = folder.Open();
        var child = Widget.Child("children").Item(0);
        Assert.Equal(["This is outside its group's box, so it's hidden"], Visibility.Problems(session, child));
        session.Apply(new NudgeWidgets([child], new SKPoint(1, 0)));
        Assert.Empty(Visibility.Problems(session, child));
        session.Undo();
        Assert.Equal(["This is outside its group's box, so it's hidden"], Visibility.Problems(session, child));
    }

    [Fact]
    public void Drag_preview_uses_the_fitted_group_and_cancelling_restores_the_warning()
    {
        using var folder = TempTheme.With(Prefix + "widgets: [" + Group(Text.Replace("[10, 10]", "[10, 70]"))
            .Replace("size: [100, 100]", "size: [396, 53]") + "]");
        using var session = folder.Open();
        var child = Widget.Child("children").Item(0);
        session.BeginPreview();
        session.PreviewMove(child, new SKPoint(11, 70));
        Assert.Empty(Visibility.Problems(session, child));
        session.CancelPreview();
        Assert.Equal(["This is outside its group's box, so it's hidden"], Visibility.Problems(session, child));
    }

    [Theory]
    [InlineData("style: { size: 24 }, spans: [{ text: Hello, style: { size: 1 } }]", "This text is 1px, too small to see")]
    [InlineData("style: { size: 1 }, spans: [{ text: Hello, style: { size: 24 } }]", null)]
    public void Text_runs_use_their_own_sizes(string settings, string? expected) =>
        Check(Text.Replace("text: Hello", settings), expected is null ? [] : [expected]);

    [Fact]
    public void Multiple_problems_have_stable_order() => Check(
        Text.Replace("text: Hello", "text: Hello, hidden: true, style: { size: 1, opacity: 0 }"),
        "This text is 1px, too small to see", "This is fully transparent", "This is hidden. Show it in Layers.");

    static string Group(string child, string settings = "", int size = 100) =>
        $"{{ type: group, at: [100, 100], size: [{size}, {size}], {settings} children: [{child}] }}";

    static void Check(string item, params string[] expected) => Check(item, expected, Widget);

    static void Check(string item, string[] expected, YamlPath path)
    {
        using var folder = TempTheme.With(Prefix + "widgets: [" + item + "]");
        using var session = folder.Open();
        Assert.Null(session.LoadError);
        Assert.Equal(expected, Visibility.Problems(session, path));
    }
}
