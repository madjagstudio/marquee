using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Renames the widget at `Widget`: its `name:`, trimmed, written in the quoting it needs, or removed when the name is
/// cleared, so the panel shows a made-up one again. Background layers are renamed in E2. The theme refuses a name over
/// its limit (NameParser.MaxLength), and the session with it.
public sealed record RenameLayer(YamlPath Widget, string Name) : EditorCommand
{
    public override string Label => "Rename";

    public override CommandResult Run(CommandContext context)
    {
        context.RequireWidget(Widget);
        var name = Name.Trim();
        var key = Widget.Child("name");
        if (name.Length > 0) return new SetValue(key, name).Run(context);
        return context.Document.Exists(key) ? context.Document.RemoveKey(key) : context.Document;
    }
}

public enum LayerFlag { Hidden, Locked }

/// Hides or shows, or locks or unlocks, the widgets and background layers at `Layers`: on writes `hidden: true` or
/// `locked: true` (setting the key if the file has it, else adding it), and off removes the key, which is false by
/// default. A layer already so is left as it is.
public sealed record SetLayerFlag(IReadOnlyList<YamlPath> Layers, LayerFlag Flag, bool On) : EditorCommand
{
    public override string Label => (Flag, On) switch
    {
        (LayerFlag.Hidden, true) => "Hide",
        (LayerFlag.Hidden, false) => "Show",
        (LayerFlag.Locked, true) => "Lock",
        _ => "Unlock",
    };

    public override CommandResult Run(CommandContext context)
    {
        var name = Flag == LayerFlag.Hidden ? "hidden" : "locked";
        var document = context.Document;
        foreach (var layer in Layers.Distinct())
        {
            if (context.Widget(layer) is null && !IsBackgroundLayer(document, layer))
                throw new ArgumentException($"There is no widget or background layer at {layer}.", nameof(Layers));

            var key = layer.Child(name);
            if (On) document = document.Exists(key) ? document.SetScalar(key, "true") : document.AddKey(layer, name, "true");
            else if (document.Exists(key)) document = document.RemoveKey(key);
        }

        return document;
    }

    static bool IsBackgroundLayer(LayoutDocument document, YamlPath path) =>
        path.Parent == YamlPath.Root.Child("background") && path.Last is { IsKey: false } && document.Exists(path);
}
