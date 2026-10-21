using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// The gradient editor's model reads any paint key's gradient and writes back only what changed: colours, stops (with
/// "spread evenly" removing them), a linear angle, a radial centre and radius.
public class GradientModelTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "background:\n" +
        "  - fill: { linear: [\"#070A12\", \"#141B2E\"], angle: 90 }\n" +
        "widgets:\n" +
        "  - type: text\n" +
        "    at: [10, 10]\n" +
        "    size: [300, 60]\n" +
        "    text: MARQUEE\n" +
        "    style:\n" +
        "      size: 40\n" +
        "      text_fill: { linear: [\"#F4F6FA\", \"#36D1DC\", \"#5B86E5\"], stops: [0, 0.3, 1] }\n" +
        "      text_stroke: { width: 2, color: { radial: [\"#FFFFFF\", \"#A770EF\"] } }\n" +
        "  - { type: bar, at: [10, 100], size: [200, 20], source: cpu.load, style: { fill: \"#36D1DC\", stroke: { width: 2, color: { linear: [\"#000000\", \"#FFFFFF\"] } } } }\n";

    static readonly YamlPath TextFill = YamlPath.Parse("widgets[0].style.text_fill");
    static readonly YamlPath StrokeColor = YamlPath.Parse("widgets[1].style.stroke.color");
    static readonly YamlPath TextStrokeColor = YamlPath.Parse("widgets[0].style.text_stroke.color");
    static readonly YamlPath LayerFill = YamlPath.Parse("background[0].fill");
    static readonly YamlPath BarFill = YamlPath.Parse("widgets[1].style.fill");

    /// Writes `edit` of the gradient at `path` and checks the text it leaves, and that undo restores the layout.
    static void AssertWrite(YamlPath path, Func<GradientModel, GradientModel> edit, string expected)
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var gradient = GradientModel.Read(session.Document, path)!;

        session.Apply(edit(gradient).Write(path));

        Assert.Equal(expected, session.Document.Text);
        Assert.Null(session.LoadError);
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
    }

    public static TheoryData<string> Examples => PropertyCatalogTests.Examples;

    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_gradient_in_the_examples_reads_and_writes_back_unchanged(string example)
    {
        using var folder = TempTheme.Example(example);
        using var session = folder.Open();
        var gradients = Gradients(session.Document.ValueAt(YamlPath.Root)!, YamlPath.Root).ToList();
        Assert.NotEmpty(gradients);

        foreach (var (path, value) in gradients)
        {
            var gradient = GradientModel.Read(value);
            Assert.NotNull(gradient);
            Assert.Null(gradient.Error);
            Assert.Equal(YamlWriter.Flow(value), YamlWriter.Flow(gradient.ToYaml()));
            Assert.Equal(YamlWriter.Flow(value), YamlWriter.Flow(GradientModel.Read(gradient.ToYaml())!.ToYaml()));

            var before = session.Document.Text;
            session.Apply(gradient.Write(path));
            Assert.Equal(before, session.Document.Text);
        }
    }

    /// A widget with a named style (`style: panel`) takes a gradient as the colour rows write a colour: the style
    /// becomes `{ extends: panel, fill: … }`, so the other widgets using panel don't change.
    [Fact]
    public void A_gradient_on_a_widget_with_a_named_style_is_written_as_an_override()
    {
        using var folder = TempTheme.Example("example-landscape");
        using var session = folder.Open();
        var fill = PropertyModel.Build(session.Document, [session.Widget(YamlPath.Parse("widgets[0]"))!]).Row("style.fill")!;
        Assert.Equal("widgets[0].style", fill.Paths[0].Parent!.ToString());
        Assert.True(session.Document.TryGetScalar(fill.Paths[0].Parent!, out var name));

        session.Apply(GradientModel.FromColor("#FF0000").SetColor(1, "#00FF00").Write(fill.Paths[0]));

        Assert.Contains($"style: {{ extends: {name}, fill: {{ linear: [\"#FF0000\", \"#00FF00\"] }} }}", session.Document.Text);
        Assert.Null(session.LoadError);
    }

    /// The review's fuzz found every gradient write to a widget with a named style refused: each paint row of each
    /// such widget in the examples now takes one, starting from the gradient it inherits when it has one.
    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_paint_row_of_a_widget_with_a_named_style_takes_a_gradient(string example)
    {
        using var folder = TempTheme.Example(example);
        using var session = folder.Open();
        var original = session.Document.Text;
        var written = 0;
        foreach (var widget in All(session.Widgets))
        {
            var style = widget.Path.Child("style");
            if (!session.Document.TryGetScalar(style, out var name) || name.Length == 0) continue;
            var model = PropertyModel.Build(session.Document, [widget]);
            foreach (var row in model.Style.SelectMany(section => section.Rows).SelectMany(Flat)
                         .Where(row => row.Kind == Marquee.Core.Themes.Schema.SchemaKind.Paint && row.IsEditable))
            {
                var gradient = GradientModel.Read(row) ?? GradientModel.FromColor("#FF0000");
                session.Apply(gradient.SetColor(1, "#00FF00").Write(row.Paths[0]));
                Assert.Null(session.LoadError);
                Assert.True(session.Document.TryGetScalar(style.Child("extends"), out var extends));
                Assert.Equal(name, extends);
                Assert.True(session.Undo());
                written++;
            }
        }

        Assert.True(written > 0);
        Assert.Equal(original, session.Document.Text);
    }

    [Fact]
    public void A_row_reads_the_gradient_it_inherits_when_it_sets_none()
    {
        const string named =
            "canvas: { orientation: landscape }\n" +
            "styles:\n" +
            "  glow: { fill: { linear: [\"#000000\", \"#FFFFFF\"], angle: 45 } }\n" +
            "widgets:\n" +
            "  - { type: bar, at: [0, 0], size: [100, 20], source: cpu.load, style: glow }\n";
        using var folder = TempTheme.With(named);
        using var session = folder.Open();
        var row = PropertyModel.Build(session.Document, [session.Widget(YamlPath.Parse("widgets[0]"))!]).Row("style.fill")!;

        var inherited = GradientModel.Read(row)!;
        Assert.Equal(["#000000", "#FFFFFF"], inherited.Colors);
        Assert.Equal(45, inherited.Angle);

        session.Apply(inherited.WithAngle(90).Write(row.Paths[0]));
        Assert.Contains("style: { extends: glow, fill: { linear: [\"#000000\", \"#FFFFFF\"], angle: 90 } } }", session.Document.Text);
    }

    static IEnumerable<WidgetRef> All(IEnumerable<WidgetRef> widgets) => widgets.SelectMany(w => All(w.Children).Prepend(w));

    static IEnumerable<PropertyRow> Flat(PropertyRow row) => row.Children.SelectMany(Flat).Prepend(row);

    [Fact]
    public void A_gradient_reads_its_keys_and_what_draws_without_them()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        var fill = GradientModel.Read(session.Document, TextFill)!;
        Assert.Equal(GradientKind.Linear, fill.Kind);
        Assert.Equal(["#F4F6FA", "#36D1DC", "#5B86E5"], fill.Colors);
        Assert.Equal([0, 0.3, 1], fill.Stops!);
        Assert.Null(fill.Angle);
        Assert.Equal(0, fill.EffectiveAngle);

        var radial = GradientModel.Read(session.Document, TextStrokeColor)!;
        Assert.Equal(GradientKind.Radial, radial.Kind);
        Assert.Null(radial.Stops);
        Assert.Equal([0, 1], radial.EffectiveStops);
        Assert.Equal((0.5, 0.5), radial.EffectiveCenter);
        Assert.Equal(1, radial.EffectiveRadius);

        Assert.Null(GradientModel.Read(session.Document, BarFill));
        Assert.Equal(90, GradientModel.Read(session.Document, LayerFill)!.Angle);
    }

    [Fact]
    public void Changing_the_angle_writes_only_the_angle() =>
        AssertWrite(LayerFill, g => g.WithAngle(45), Layout.Replace("angle: 90", "angle: 45"));

    [Fact]
    public void Colours_are_recoloured_added_removed_and_reordered()
    {
        AssertWrite(StrokeColor, g => g.SetColor(1, "#FF0000"), Layout.Replace("[\"#000000\", \"#FFFFFF\"]", "[\"#000000\", \"#FF0000\"]"));
        AssertWrite(StrokeColor, g => g.AddColor("#FF0000"), Layout.Replace("[\"#000000\", \"#FFFFFF\"]", "[\"#000000\", \"#FFFFFF\", \"#FF0000\"]"));
        AssertWrite(StrokeColor, g => g.MoveColor(1, 0), Layout.Replace("[\"#000000\", \"#FFFFFF\"]", "[\"#FFFFFF\", \"#000000\"]"));
        AssertWrite(TextFill, g => g.RemoveColor(1),
            Layout.Replace("[\"#F4F6FA\", \"#36D1DC\", \"#5B86E5\"], stops: [0, 0.3, 1]", "[\"#F4F6FA\", \"#5B86E5\"], stops: [0, 1]"));
        // A colour added between two with stops sits halfway between them.
        AssertWrite(TextFill, g => g.AddColor("#000000", 1),
            Layout.Replace("[\"#F4F6FA\", \"#36D1DC\", \"#5B86E5\"], stops: [0, 0.3, 1]", "[\"#F4F6FA\", \"#000000\", \"#36D1DC\", \"#5B86E5\"], stops: [0, 0.15, 0.3, 1]"));
    }

    [Fact]
    public void A_gradient_keeps_its_fewest_colours()
    {
        var gradient = GradientModel.FromColor("#FF0000");
        Assert.Equal(["#FF0000", "#FF0000"], gradient.Colors);
        Assert.Throws<InvalidOperationException>(() => gradient.RemoveColor(0));
    }

    [Fact]
    public void Stops_are_written_and_spread_evenly_removes_them()
    {
        AssertWrite(StrokeColor, g => g.SetStop(0, 0.25), Layout.Replace("[\"#000000\", \"#FFFFFF\"] }", "[\"#000000\", \"#FFFFFF\"], stops: [0.25, 1] }"));
        AssertWrite(TextFill, g => g.SpreadEvenly(), Layout.Replace(", stops: [0, 0.3, 1]", ""));
        AssertWrite(TextFill, g => g.SetStop(1, 0.6), Layout.Replace("stops: [0, 0.3, 1]", "stops: [0, 0.6, 1]"));
    }

    [Fact]
    public void A_stop_is_kept_between_its_neighbours()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var fill = GradientModel.Read(session.Document, TextFill)!;

        Assert.Equal([0, 1, 1], fill.SetStop(1, 1.4).Stops!);
        Assert.Equal([0, 0, 1], fill.SetStop(1, -3).Stops!);
        Assert.Equal([0, 0.2, 0.3], fill.SetStop(2, 0.2).SetStop(1, 0.2).Stops!);
        Assert.Equal("each stop must be no lower than the one before",
            GradientModel.Read(YamlValue.Parse("{ linear: [\"#000000\", \"#FFFFFF\"], stops: [0.6, 0.4] }"))!.Error);
    }

    [Fact]
    public void A_radials_centre_and_radius_are_written()
    {
        AssertWrite(TextStrokeColor, g => g.WithCenter(0.3, 0.4).WithRadius(0.8),
            Layout.Replace("{ radial: [\"#FFFFFF\", \"#A770EF\"] }", "{ radial: [\"#FFFFFF\", \"#A770EF\"], center: [0.3, 0.4], radius: 0.8 }"));
        Assert.Equal("radius must be between 0 and 3", GradientModel.FromColor("#FFFFFF", GradientKind.Radial).WithRadius(3.5).Error);
        Assert.Null(GradientModel.FromColor("#FFFFFF", GradientKind.Radial).WithRadius(GradientModel.MaxRadius).Error);
        Assert.Equal("center must be two numbers between 0 and 1", GradientModel.FromColor("#FFFFFF", GradientKind.Radial).WithCenter(1.2, 0).Error);
        Assert.Throws<PropertyValueException>(() => GradientModel.FromColor("#FFFFFF", GradientKind.Radial).WithRadius(4).Write(TextStrokeColor));
    }

    [Fact]
    public void Switching_the_kind_drops_what_the_other_kind_takes()
    {
        AssertWrite(LayerFill, g => g.WithKind(GradientKind.Radial), Layout.Replace("{ linear: [\"#070A12\", \"#141B2E\"], angle: 90 }", "{ radial: [\"#070A12\", \"#141B2E\"] }"));
        var radial = GradientModel.FromColor("#FFFFFF", GradientKind.Radial).WithCenter(0.2, 0.2).WithRadius(2);
        var linear = radial.WithKind(GradientKind.Linear);
        Assert.Null(linear.Center);
        Assert.Null(linear.Radius);
    }

    [Fact]
    public void A_color_becomes_a_gradient_and_back()
    {
        AssertWrite(BarFill, _ => GradientModel.FromColor("#36D1DC").SetColor(1, "#5B86E5"),
            Layout.Replace("fill: \"#36D1DC\"", "fill: { linear: [\"#36D1DC\", \"#5B86E5\"] }"));

        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        session.Select([YamlPath.Parse("widgets[1]")]);
        var model = PropertyModel.For(session);
        session.Apply(model.Set(model.Row("style.stroke.color")!, GradientModel.Read(session.Document, StrokeColor)!.Colors[0]));
        Assert.Contains("stroke: { width: 2, color: \"#000000\" }", session.Document.Text);
    }

    [Fact]
    public void A_drag_of_a_stop_is_one_step()
    {
        var time = new ManualTime();
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open(new EditorSessionOptions { Time = time });

        foreach (var stop in new[] { 0.4, 0.5, 0.6 })
        {
            session.Apply(GradientModel.Read(session.Document, TextFill)!.SetStop(1, stop).Write(TextFill, merge: true));
            time.Advance(TimeSpan.FromMilliseconds(30));
        }

        Assert.Contains("stops: [0, 0.6, 1]", session.Document.Text);
        Assert.True(session.Undo());
        Assert.Equal(Layout, session.Document.Text);
    }

    /// Every gradient a file holds, with its path: any mapping with `linear:` or `radial:`.
    static IEnumerable<(YamlPath Path, YamlValue Value)> Gradients(YamlValue value, YamlPath path) => value switch
    {
        YamlMap map when map["linear"] is not null || map["radial"] is not null => [(path, value)],
        YamlMap map => map.Entries.SelectMany(e => Gradients(e.Value, path.Child(e.Key))),
        YamlList list => list.Items.SelectMany((item, i) => Gradients(item, path.Item(i))),
        _ => [],
    };
}
