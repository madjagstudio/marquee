using System.Globalization;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Adds a widget of `Type` (one of ThemeSchema.Widgets' names): the catalog's example for it, placed in the middle of
/// the canvas, or of the group at `Group` (the one being worked in). It goes last in its list, so it draws on top, and
/// becomes the selection. A theme or group without its list gains one.
public sealed record AddWidget(string Type, YamlPath? Group = null) : EditorCommand
{
    public override string Label => $"Add {Type}";

    public override CommandResult Run(CommandContext context)
    {
        var example = ThemeSchema.Widget(Type)?.Example is { } yaml
            ? (YamlMap)YamlValue.Parse(yaml)
            : throw new ArgumentException($"There is no '{Type}' widget.", nameof(Type));

        var (list, count, space) = Group is null
            ? (YamlPath.Root.Child("widgets"), context.Widgets.Count, (SKSize)context.CanvasSize)
            : Into(context.RequireWidget(Group));
        var size = Size(example);
        // Whole pixels, as themes are written: a box an odd number of pixels narrower than its space sits half a pixel
        // to the left.
        var at = new YamlList(
        [
            YamlValue.Number(Math.Floor((space.Width - size.Width) / 2)),
            YamlValue.Number(Math.Floor((space.Height - size.Height) / 2)),
        ]);
        var widget = example.With("at", at);

        var document = context.Document.Exists(list)
            ? context.Document.InsertItem(list, count, YamlWriter.Write(widget))
            : context.Document.AddKey(list.Parent!, list.Last!.Value.Key!, YamlWriter.Write(new YamlList([widget])));
        return new CommandResult(GroupFit.Around(document, [list.Item(count)]), [list.Item(count)]);
    }

    /// A group's list of children, how many it has, and the size of the box they are placed in.
    static (YamlPath List, int Count, SKSize Space) Into(WidgetRef group) =>
        group.IsGroup
            ? (group.Path.Child("children"), group.Children.Count, group.LocalBounds.Size)
            : throw new ArgumentException($"{group.Path} isn't a group.", nameof(Group));

    /// The example's `size`: every widget type's example has one.
    static (double Width, double Height) Size(YamlMap example) =>
        example["size"] is YamlList { Items: [YamlText width, YamlText height] }
            ? (Number(width), Number(height))
            : throw new InvalidOperationException("A widget example has no size.");

    static double Number(YamlText text) => double.Parse(text.Value, CultureInfo.InvariantCulture);
}
