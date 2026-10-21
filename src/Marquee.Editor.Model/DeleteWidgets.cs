using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Deletes the widgets at `Widgets`, with their comments (RemoveItem), and clears the selection. A widget inside a
/// group that is also being deleted goes with the group. Each must be a widget: anything else is an ArgumentException.
public sealed record DeleteWidgets(IReadOnlyList<YamlPath> Widgets) : EditorCommand
{
    public override string Label => Widgets.Count == 1 ? "Delete" : $"Delete {Widgets.Count} widgets";

    public override CommandResult Run(CommandContext context)
    {
        foreach (var path in Widgets) context.RequireWidget(path);

        // Last in the file first, so each removal leaves the paths of those still to go as they were.
        var document = context.Document;
        var outermost = Widgets.Distinct().Where(path => !Widgets.Any(group => Inside(path, group)));
        foreach (var path in outermost.OrderByDescending(path => path, FileOrder.Instance))
            document = document.RemoveItem(path);
        return new CommandResult(document, []);
    }

    /// True when `path` is within the widget at `group` (and isn't it).
    static bool Inside(YamlPath path, YamlPath group) =>
        path.Segments.Count > group.Segments.Count && path.Segments.Take(group.Segments.Count).SequenceEqual(group.Segments);
}

/// Orders paths as the nodes they name come in the file, for paths through the same lists: by index where they part
/// at a list, and a node before what is inside it.
sealed class FileOrder : IComparer<YamlPath>
{
    public static FileOrder Instance { get; } = new();

    public int Compare(YamlPath? x, YamlPath? y)
    {
        var (a, b) = (x?.Segments ?? [], y?.Segments ?? []);
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            var order = (a[i].IsKey, b[i].IsKey) switch
            {
                (false, false) => a[i].Index.CompareTo(b[i].Index),
                (true, true) => string.CompareOrdinal(a[i].Key, b[i].Key),
                _ => a[i].IsKey ? 1 : -1,
            };
            if (order != 0) return order;
        }

        return a.Count.CompareTo(b.Count);
    }
}
