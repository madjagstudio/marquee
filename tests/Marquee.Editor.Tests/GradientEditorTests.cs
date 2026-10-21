using System.Windows;
using Marquee.Editor.Gradients;
using Marquee.Editor.Model;
using Marquee.Editor.Tests.TestSupport;
using static Marquee.Editor.Tests.TestSupport.OpenEditor;

namespace Marquee.Editor.Tests;

/// The gradient editor, opened from a paint row: dragging a stop writes `stops` as one undo step, a click adds a stop
/// in the colour the gradient has there, dragging one off removes it, and the angle, kind, centre and radius write
/// their keys.
public class GradientEditorTests
{
    static GradientEditorViewModel Open(OpenEditor editor)
    {
        editor.Session.Select([P("widgets[3]")]);
        var fill = editor.Model.Properties.Row("style.fill")!;
        Assert.True(fill.IsGradient);
        fill.IsGradientOpen = true;
        return fill.Gradient!;
    }

    static string Text(OpenEditor editor) => editor.Session.Document.Text;

    [Fact]
    public void Dragging_a_stop_writes_stops_as_one_undo_step() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = PropertiesPanelTests.Open(environment);
        var gradient = Open(editor);
        Assert.Equal([0, 0.5, 1], gradient.Stops.Select(stop => stop.Position));

        gradient.BeginStopDrag(1);
        gradient.DragStop(0.3, away: 0);
        gradient.DragStop(0.2512, away: 3);
        // A stop stays between its neighbours however far it is dragged.
        gradient.DragStop(-0.5, away: 0);
        gradient.DragStop(0.25, away: 0);
        gradient.EndStopDrag();

        Assert.Contains("linear: [\"#000000\", \"#808080\", \"#FFFFFF\"], stops: [0, 0.25, 1] }", Text(editor));
        Assert.Equal(1, editor.Session.History.Count);
        Assert.Equal(1, gradient.Selected);
    });

    [Fact]
    public void A_resting_stop_drag_is_one_undo_step_saved_after_release() => Sta.Run(() =>
    {
        var clock = new FakeClock();
        using var environment = new TestEnvironment(clock);
        using var editor = PropertiesPanelTests.Open(environment);
        var disk = new PropertiesPanelTests.Disk(environment, editor);
        var gradient = Open(editor);

        gradient.BeginStopDrag(1);
        gradient.DragStop(0.3, away: 0);
        clock.Advance(TimeSpan.FromSeconds(3));
        gradient.DragStop(0.25, away: 0);
        clock.Advance(TimeSpan.FromSeconds(3));
        Sta.Pump();
        Assert.Equal(PropertiesPanelTests.Layout, disk.Text);

        gradient.EndStopDrag();
        clock.Advance(TimeSpan.FromSeconds(1));
        Sta.Pump();

        Assert.Contains("stops: [0, 0.25, 1] }", disk.Text);
        Assert.Equal(1, editor.Session.History.Count);
    });

    [Fact]
    public void A_click_adds_a_stop_in_the_colour_there_and_dragging_it_off_removes_it() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = PropertiesPanelTests.Open(environment);
        var gradient = Open(editor);

        gradient.AddStopAt(0.75);

        // Halfway from grey (0x80) to white: 0xBF or 0xC0.
        Assert.Contains("linear: [\"#000000\", \"#808080\", \"#C0C0C0\", \"#FFFFFF\"], stops: [0, 0.5, 0.75, 1]", Text(editor));
        Assert.Equal(2, gradient.Selected);

        gradient.BeginStopDrag(2);
        gradient.DragStop(0.8, away: GradientEditorViewModel.TearOff + 10);
        Assert.True(gradient.Stops[2].IsLeaving);
        gradient.EndStopDrag();

        Assert.Contains("linear: [\"#000000\", \"#808080\", \"#FFFFFF\"], stops: [0, 0.5, 1]", Text(editor));
        Assert.Equal(2, editor.Session.History.Count);

        // Two colours are the fewest: then a stop can't come off, and Del does nothing.
        Assert.True(gradient.RemoveSelected());
        Assert.False(gradient.CanRemove);
        gradient.BeginStopDrag(0);
        gradient.DragStop(0, away: 100);
        gradient.EndStopDrag();
        Assert.False(gradient.RemoveSelected());
        Assert.Equal(2, gradient.Gradient.Colors.Count);
    });

    [Fact]
    public void Recolouring_spreading_the_angle_and_the_kind_write_their_keys() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = PropertiesPanelTests.Open(environment);
        var gradient = Open(editor);

        gradient.SetColor(1, "#FF0000", final: false);
        gradient.SetColor(1, "#FF000080", final: true);
        Assert.Contains("linear: [\"#000000\", \"#FF000080\", \"#FFFFFF\"]", Text(editor));
        Assert.Equal(1, editor.Session.History.Count);

        gradient.BeginStopDrag(1);
        gradient.DragStop(0.4, away: 0);
        gradient.EndStopDrag();
        gradient.SpreadEvenly();
        Assert.DoesNotContain("stops", Text(editor));

        gradient.SetAngle(45, final: false);
        gradient.SetAngle(90, final: true);
        Assert.Contains("angle: 90", Text(editor));
        gradient.AngleText = "135";
        Assert.True(gradient.CommitAngle());
        Assert.Contains("angle: 135", Text(editor));

        gradient.Kind = GradientKind.Radial;
        Assert.Contains("fill: { radial: [\"#000000\", \"#FF000080\", \"#FFFFFF\"] }", Text(editor));
        gradient.SetCenter(0.25, 0.75, final: false);
        gradient.SetRadius(1.5, final: true);
        Assert.Contains("center: [0.25, 0.75]", Text(editor));
        Assert.Contains("radius: 1.5", Text(editor));
    });

    [Fact]
    public void The_dial_and_the_preview_turn_points_into_angles_centres_and_radii()
    {
        var centre = new Point(28, 28);
        Assert.Equal(0, GradientEditorViewModel.AngleAt(centre, new Point(50, 28), snap: false));
        Assert.Equal(90, GradientEditorViewModel.AngleAt(centre, new Point(28, 50), snap: false));
        Assert.Equal(180, GradientEditorViewModel.AngleAt(centre, new Point(0, 28), snap: false));
        Assert.Equal(270, GradientEditorViewModel.AngleAt(centre, new Point(28, 0), snap: false));
        Assert.Equal(15, GradientEditorViewModel.AngleAt(centre, new Point(48, 34), snap: true));

        Assert.Equal((0.25, 0.5), GradientEditorViewModel.CenterAt(new Point(30, 30), new Size(120, 60)));
        Assert.Equal((1, 0), GradientEditorViewModel.CenterAt(new Point(500, -5), new Size(120, 60)));
        // Half the longer side is a radius of 1.
        Assert.Equal(1, GradientEditorViewModel.RadiusAt(new Point(60, 30), new Point(120, 30), new Size(120, 60)));
        Assert.Equal(0.5, GradientEditorViewModel.PositionAt(50, 100));
        Assert.Equal(1, GradientEditorViewModel.PositionAt(150, 100));
    }
}
