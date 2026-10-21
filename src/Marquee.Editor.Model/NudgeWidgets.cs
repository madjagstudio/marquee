using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

public enum ArrowKey { Left, Right, Up, Down }

/// Moves the selection by `By`, in the coordinates each widget's `at` is written in (its group's), as the arrow keys
/// do: 1 px, or 10 with Shift (Arrow). A run of nudges to one selection is one undo step: they share a merge key, so
/// those within the undo stack's merge window of each other join. Locked widgets, and those using an anchor or alias,
/// stay where they are, and a widget inside another being nudged goes with it.
public sealed record NudgeWidgets : EditorCommand
{
    public const float Step = 1;
    public const float ShiftStep = 10;

    public NudgeWidgets(IReadOnlyList<YamlPath> widgets, SKPoint by)
    {
        Widgets = widgets;
        By = by;
        MergeKey = "Nudge " + string.Join(' ', widgets.Distinct());
    }

    public IReadOnlyList<YamlPath> Widgets { get; }

    public SKPoint By { get; }

    public override string Label => "Nudge";

    /// The nudge an arrow key makes.
    public static NudgeWidgets Arrow(IReadOnlyList<YamlPath> widgets, ArrowKey key, bool shift)
    {
        var step = shift ? ShiftStep : Step;
        return new NudgeWidgets(widgets, key switch
        {
            ArrowKey.Left => new SKPoint(-step, 0),
            ArrowKey.Right => new SKPoint(step, 0),
            ArrowKey.Up => new SKPoint(0, -step),
            _ => new SKPoint(0, step),
        });
    }

    public override CommandResult Run(CommandContext context)
    {
        var placements = Arrange.Movable(CanvasGeometry.Of(context), Widgets)
            .Select(w => new WidgetPlacement(w.Path, CanvasMath.Moved(w.LocalBounds, By), w.Rotate))
            .ToList();
        return new PlaceWidgets(placements) { Description = Label }.Run(context);
    }
}
