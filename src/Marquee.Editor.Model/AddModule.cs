using System.Globalization;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// A module is ordinary widget YAML. Only its position changes; insertion keeps the surrounding file's bytes.
public sealed record AddModule(Module Module, SKPoint? At = null, YamlPath? Group = null) : EditorCommand
{
    public override string Label => Module.Name;
    public override CommandResult Run(CommandContext context)
    {
        var snippet = LayoutDocument.Open(Module.Yaml);
        var root = YamlPath.Root;
        var size = ModuleLibrary.Size(Module);
        var space = (SKSize)context.CanvasSize;
        if (Group is not null)
        {
            var group = context.RequireWidget(Group);
            if (!group.IsGroup || CanvasGeometry.Of(context).IsLocked(Group)) throw new ArgumentException(ModuleText.Get("GroupUnavailable"));
            space = group.LocalBounds.Size;
        }
        var point = At ?? new SKPoint((float)Math.Floor((space.Width - size.Width) / 2), (float)Math.Floor((space.Height - size.Height) / 2));
        var at = root.Child("at");
        var value = $"[{point.X.ToString(CultureInfo.InvariantCulture)}, {point.Y.ToString(CultureInfo.InvariantCulture)}]";
        snippet = snippet.Exists(at) ? snippet.ReplaceValue(at, value) : snippet.AddKey(root, "at", value);
        var list = Group?.Child("children") ?? root.Child("widgets");
        var document = context.Document;
        if (!document.Exists(list)) document = document.AddKey(list.Parent!, list.Last!.Value.Key!, "[]");
        var index = document.GetNode(list)!.Count;
        document = document.InsertItem(list, index, snippet.Text, asWritten: true);
        var added = list.Item(index);
        return new(GroupFit.Around(document, [added]), [added]);
    }
}
