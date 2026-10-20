using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Working inside a group. Double-clicking a group enters it: clicks then hit its children, positions are the group's
/// (ToLocal), and new and pasted widgets go into it. Esc goes back out a level (GroupOf), and a click outside it leaves
/// it for the group around the click, or the canvas. The canvas view keeps which group it is in.
public sealed partial class CanvasGeometry
{
    /// The group a double-click at `point` enters, from `group` (null for the canvas): the group hit there among its
    /// widgets; null when what is hit isn't a group, or nothing is.
    public YamlPath? GroupToEnter(SKPoint point, YamlPath? group) =>
        HitTest(point, group) is { IsGroup: true } hit ? hit.Path : null;

    /// The group a click at `point` leaves the canvas in, from inside `group`: `group` itself when the click is inside
    /// its box, else the nearest group around it whose box holds the click, else null for the canvas.
    public YamlPath? GroupAfterClick(SKPoint point, YamlPath? group)
    {
        for (var at = group; at is not null; at = GroupOf(at))
            if (Contains(Require(at), point, 0)) return at;
        return null;
    }
}
