using System.Globalization;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Where a widget goes: its box and turn as `at`, `size` and `rotate` write them, in its group's coordinates (the
/// canvas's for a top-level widget).
public readonly record struct WidgetPlacement(YamlPath Widget, SKRect Bounds, float Rotate);

/// Moves, resizes and turns widgets, writing only the numbers that change: a drag let go (CommitPreview), a nudge, or
/// align and distribute. `at` and `size` keep their style, number by number, and `rotate` is added when the widget
/// hadn't one. Numbers are written as given, as short as they read back (`110.5`): snapping is the caller's.
public sealed record PlaceWidgets(IReadOnlyList<WidgetPlacement> Placements) : EditorCommand
{
    /// "Move", "Resize" or "Rotate", or whatever the caller names it ("Align left").
    public string Description { get; init; } = "Move";

    public override string Label => Description;

    public override CommandResult Run(CommandContext context)
    {
        var document = context.Document;
        foreach (var placement in Placements)
        {
            var widget = context.RequireWidget(placement.Widget);
            var (from, to) = (widget.LocalBounds, placement.Bounds);
            document = Pair(document, widget.Path, "at", from.Location, to.Location);
            document = Pair(document, widget.Path, "size", new SKPoint(from.Width, from.Height),
                new SKPoint(to.Width, to.Height));
            if (placement.Rotate != widget.Rotate)
            {
                var rotate = widget.Path.Child("rotate");
                document = document.Exists(rotate)
                    ? document.SetScalar(rotate, Number(placement.Rotate))
                    : document.AddKey(widget.Path, "rotate", Number(placement.Rotate));
            }
        }

        return document;
    }

    /// Writes `to` as the pair at `key`, written `from` now: each number that changed in its place in a `[x, y]`, and
    /// the whole pair otherwise.
    static LayoutDocument Pair(LayoutDocument document, YamlPath widget, string key, SKPoint from, SKPoint to)
    {
        if (from == to) return document;
        var path = widget.Child(key);
        if (document.GetNode(path) is { Kind: LayoutNodeKind.List, Count: 2 }
            && document.TryGetScalar(path.Item(0), out _) && document.TryGetScalar(path.Item(1), out _))
        {
            if (from.X != to.X) document = document.SetScalar(path.Item(0), Number(to.X));
            if (from.Y != to.Y) document = document.SetScalar(path.Item(1), Number(to.Y));
            return document;
        }

        var pair = YamlWriter.Flow(new YamlList([Number(to.X), Number(to.Y)]));
        return document.Exists(path) ? document.ReplaceValue(path, pair) : document.AddKey(widget, key, pair);
    }

    /// Invariant, as short as reads back the same, and 0 without a sign.
    internal static string Number(float value) => value == 0 ? "0" : value.ToString(CultureInfo.InvariantCulture);
}
