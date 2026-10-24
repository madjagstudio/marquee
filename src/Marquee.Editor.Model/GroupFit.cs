using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Parsing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Grows a group's clipping box around its children, preserving their places on the canvas. Fitting and the edit
/// which needed it return one document, so undo restores both together.
public static class GroupFit
{
    public static LayoutDocument Fit(LayoutDocument doc, YamlPath group)
    {
        for (YamlPath? at = group; at is not null; at = CanvasGeometry.GroupOf(at)) doc = FitOne(doc, at);
        return doc;
    }

    /// Fit all affected groups from the deepest outwards, after every placement/insertion has been written.
    internal static LayoutDocument Around(LayoutDocument doc, IEnumerable<YamlPath> widgets)
    {
        var groups = new HashSet<YamlPath>();
        foreach (var widget in widgets)
        {
            var first = doc.TryGetScalar(widget.Child("type"), out var type) && type == "group"
                ? widget : CanvasGeometry.GroupOf(widget);
            for (var at = first; at is not null; at = CanvasGeometry.GroupOf(at)) groups.Add(at);
        }
        foreach (var group in groups.OrderByDescending(p => p.Segments.Count)) doc = FitOne(doc, group);
        return doc;
    }

    static LayoutDocument FitOne(LayoutDocument doc, YamlPath path)
    {
        if (!doc.Exists(path.Child("size"))) return doc;
        var tree = WidgetTree.Build(doc, LayoutParser.Parse(doc.Text));
        if (tree.Find(path) is not { IsGroup: true } group) return doc;
        var (bounds, offset) = Measure(group.LocalBounds, group.Rotate,
            group.Children.Select(w => (w.LocalBounds, w.Rotate)));
        doc = PlaceWidgets.Place(doc, path, group.LocalBounds, group.Rotate, bounds, group.Rotate);
        if (offset == SKPoint.Empty) return doc;
        foreach (var child in group.Children)
        {
            var moved = child.LocalBounds;
            moved.Offset(offset);
            doc = PlaceWidgets.Place(doc, child.Path, child.LocalBounds, child.Rotate, moved, child.Rotate);
        }
        return doc;
    }

    /// The same local rotations CanvasGeometry uses, before any parent transform. Used by both YAML edits and drag
    /// frames so the child remains visible before release. Growth changes the rotation centre: compensate for that
    /// as well as the child offset, otherwise growing a turned group would move all its children on screen.
    internal static (SKRect Bounds, SKPoint Offset) Measure(SKRect box, float rotate,
        IEnumerable<(SKRect Bounds, float Rotate)> children)
    {
        var extent = SKRect.Create(0, 0, box.Width, box.Height);
        foreach (var (bounds, turn) in children)
            extent = SKRect.Union(extent, SKMatrix.CreateRotationDegrees(turn, bounds.MidX, bounds.MidY).MapRect(bounds));
        const float error = 0.0001f;
        var left = extent.Left < -error ? MathF.Floor(extent.Left) : 0;
        var top = extent.Top < -error ? MathF.Floor(extent.Top) : 0;
        var right = extent.Right > box.Width + error ? MathF.Ceiling(extent.Right) : box.Width;
        var bottom = extent.Bottom > box.Height + error ? MathF.Ceiling(extent.Bottom) : box.Height;
        var offset = new SKPoint(-left, -top);
        var size = new SKSize(right - left, bottom - top);
        if (size == box.Size) return (box, SKPoint.Empty);
        var halfGrowth = new SKPoint((size.Width - box.Width) / 2, (size.Height - box.Height) / 2);
        var shifted = halfGrowth - offset;
        var rotated = SKMatrix.CreateRotationDegrees(rotate).MapVector(shifted.X, shifted.Y);
        return (SKRect.Create(box.Location - halfGrowth + rotated, size), offset);
    }
}
