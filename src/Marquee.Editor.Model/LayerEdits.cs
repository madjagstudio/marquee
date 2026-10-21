using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// What the layers panel's commands share: the list a widget is in, paths kept up to date as items come and go, and a
/// widget's box written in another group's coordinates so it stays where it is on screen.
static class LayerEdits
{
    /// The top-level `widgets` list, or the `children` of the group at `group`.
    public static YamlPath ListOf(YamlPath? group) => group?.Child("children") ?? YamlPath.Root.Child("widgets");

    /// True when `path` is inside the widget at `group` (and isn't it).
    public static bool Inside(YamlPath path, YamlPath group) =>
        path.Segments.Count > group.Segments.Count
        && path.Segments.Take(group.Segments.Count).SequenceEqual(group.Segments);

    /// `paths` without any inside another of them, which go with it, in the order given.
    public static List<YamlPath> Outermost(IEnumerable<YamlPath> paths)
    {
        var distinct = paths.Distinct().ToList();
        return distinct.Where(path => !distinct.Any(other => Inside(path, other))).ToList();
    }

    /// Where `widget` goes, as `at`, `size` and `rotate` write it, once it counts from the group at `to` (the canvas when
    /// null) rather than its own: the same place and turn on screen. Its centre is carried over and the turns of the
    /// groups it leaves and enters are added to its own, so a widget leaving a turned group turns as the group did.
    /// Numbers are kept to two decimals, enough for screen pixels and clear of float error.
    public static (SKRect Bounds, float Rotate) Rebased(CanvasGeometry geometry, WidgetRef widget, YamlPath? to)
    {
        var from = CanvasGeometry.GroupOf(widget.Path);
        var map = CanvasGeometry.Inverse(geometry.ChildFrame(to)).PreConcat(geometry.ChildFrame(from));
        var turn = TurnOf(geometry, from) - TurnOf(geometry, to);
        return Rebased(widget, map, turn);
    }

    /// `widget`'s box carried to coordinates `map` gives from its own, turned `turn` degrees more.
    public static (SKRect Bounds, float Rotate) Rebased(WidgetRef widget, SKMatrix map, float turn)
    {
        var box = widget.LocalBounds;
        var centre = map.MapPoint(box.MidX, box.MidY);
        var rotate = turn == 0 ? widget.Rotate : CanvasMath.Normalised(Rounded(widget.Rotate + turn));
        return (SKRect.Create(Rounded(centre.X - box.Width / 2), Rounded(centre.Y - box.Height / 2), box.Width, box.Height), rotate);
    }

    /// `value` to two decimals, and a whole number when it is within 0.01 of one: a trip into a turned group and out
    /// again loses up to 0.005 each way to the rounding, which would otherwise creep a whole position by 0.01 now and
    /// then.
    static float Rounded(float value)
    {
        var rounded = MathF.Round(value, 2);
        var whole = MathF.Round(rounded);
        return MathF.Abs(rounded - whole) <= 0.0101f ? whole : rounded;
    }

    /// `path` once the items from `index` on in the list at `list` have moved `by` places (an item inserted before them,
    /// or one before them removed). Paths that don't go through those items are as they were.
    public static YamlPath Shifted(YamlPath path, YamlPath list, int index, int by)
    {
        if (!Through(path, list, out var at) || at < index) return path;
        return Replaced(path, list.Segments.Count + 1, list.Item(at + by));
    }

    /// `path` with its first `count` segments replaced by `prefix`.
    public static YamlPath Replaced(YamlPath path, int count, YamlPath prefix) =>
        path.Segments.Skip(count).Aggregate(prefix, (p, segment) => segment.Key is { } key ? p.Child(key) : p.Item(segment.Index));

    /// True when `path` leads through the list at `list`, giving the index of the item it goes through.
    public static bool Through(YamlPath path, YamlPath list, out int index)
    {
        index = -1;
        var n = list.Segments.Count;
        if (path.Segments.Count <= n || !path.Segments.Take(n).SequenceEqual(list.Segments) || path.Segments[n].IsKey)
            return false;
        index = path.Segments[n].Index;
        return true;
    }

    static float TurnOf(CanvasGeometry geometry, YamlPath? group) => group is null ? 0 : geometry.TurnOf(group);
}
