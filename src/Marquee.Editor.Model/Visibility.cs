using System.Globalization;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Explains authored settings that make a widget hard to see, in a stable order for the canvas and status line.
public static class Visibility
{
    public static IReadOnlyList<string> Problems(EditorSession session, YamlPath widget)
    {
        var geometry = new CanvasGeometry(session.DrawnWidgets, session.CanvasSize);
        if (geometry.Widget(widget) is not { } selected) return [];
        var ancestors = new List<WidgetRef>();
        for (var parent = CanvasGeometry.GroupOf(widget); parent is not null; parent = CanvasGeometry.GroupOf(parent))
            if (geometry.Widget(parent) is { } group) ancestors.Add(group);

        var style = StyleSpec.Empty;
        foreach (var group in ancestors.AsEnumerable().Reverse())
            style = StyleSpec.Merge(style, group.Spec.Style).Inherited();
        style = StyleSpec.Merge(style, selected.Spec.Style);

        var problems = new List<string>();
        var size = style.Size ?? StyleDefaults.Size;
        var spans = selected.Spec switch { SpansWidget text => text.Spans, ValueWidget value => value.Spans, _ => null };
        if (spans is { Count: > 0 }) size = spans.Min(span => span.Style.Size ?? size);
        if (selected.Spec is TextWidget or ValueWidget or SpansWidget && size < 6)
            problems.Add($"This text is {size.ToString("0.###", CultureInfo.InvariantCulture)}px, too small to see");

        var opacity = style.Opacity ?? StyleDefaults.Opacity;
        foreach (var group in ancestors) opacity *= group.Spec.Style.Opacity ?? StyleDefaults.Opacity;
        if (opacity < 0.05f) problems.Add("This is fully transparent");

        var corners = geometry.CornersOf(widget);
        var groupClipping = 0;
        foreach (var group in ancestors)
        {
            var local = corners.Select(p => geometry.ToLocal(p, group.Path)).ToArray();
            groupClipping = Math.Max(groupClipping, Clipping(local, SKRect.Create(group.LocalBounds.Size)));
        }
        if (groupClipping > 0)
            problems.Add(groupClipping == 2 ? "This is outside its group's box, so it's hidden"
                : "This is partly outside its group's box, so it's partly hidden");

        var screenClipping = Clipping(corners, SKRect.Create(session.CanvasSize.Width, session.CanvasSize.Height));
        if (screenClipping > 0)
            problems.Add(screenClipping == 2 ? "This is outside the screen" : "This is partly outside the screen");
        if (geometry.IsHidden(widget)) problems.Add("This is hidden. Show it in Layers.");
        return problems;
    }

    // 0: contained, 1: partly outside, 2: wholly outside. Test both rectangles' separating axes, not just the
    // upright bounding box: a turned widget can miss a corner of the clipping box despite overlapping its bounds.
    static int Clipping(SKPoint[] corners, SKRect box)
    {
        const float error = 0.001f;
        if (corners.All(p => p.X >= box.Left - error && p.X <= box.Right + error
            && p.Y >= box.Top - error && p.Y <= box.Bottom + error)) return 0;
        SKPoint[] clip = [new(box.Left, box.Top), new(box.Right, box.Top), new(box.Right, box.Bottom), new(box.Left, box.Bottom)];
        var edge1 = corners[1] - corners[0];
        var edge2 = corners[3] - corners[0];
        SKPoint[] axes = [new(1, 0), new(0, 1), new(-edge1.Y, edge1.X), new(-edge2.Y, edge2.X)];
        foreach (var axis in axes)
        {
            var length = MathF.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
            if (length < error) continue;
            float Project(SKPoint p) => (p.X * axis.X + p.Y * axis.Y) / length;
            if (corners.Max(Project) <= clip.Min(Project) + error || clip.Max(Project) <= corners.Min(Project) + error)
                return 2;
        }
        return 1;
    }
}
