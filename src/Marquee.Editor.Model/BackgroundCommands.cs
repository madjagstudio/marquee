using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

public enum LayerKindChoice { Fill, Image, Video, Shader }

/// Adds a layer at Index (0 is the bottom), selecting it. File is theme-relative, or null for a black fill.
public sealed record AddLayer(LayerKindChoice Kind, string? File, int Index) : EditorCommand
{
    public override string Label => "Add layer";

    /// Just above the selected background layer, or on top of the background when none is selected.
    public static int AddIndex(EditorSession session) => session.SelectedLayer is { } layer
        ? layer.Last!.Value.Index + 1
        : session.Document.GetNode(BackgroundEdits.List)?.Count ?? 0;

    public override CommandResult Run(CommandContext context)
    {
        var (key, value) = BackgroundEdits.Content(Kind, File);
        var document = context.Document;
        if (!document.Exists(BackgroundEdits.List))
            document = document.AddKey(YamlPath.Root, "background", "[]");
        document = document.InsertItem(BackgroundEdits.List, Index, $"{key}: {value}");
        return new CommandResult(document, [BackgroundEdits.List.Item(Index)]);
    }
}

/// Removes a background layer and clears selection. Its imported files stay for unused-file cleanup.
public sealed record RemoveLayer(YamlPath Layer) : EditorCommand
{
    public override string Label => "Delete layer";

    public override CommandResult Run(CommandContext context)
    {
        BackgroundEdits.RequireEditable(context.Document, Layer);
        return new CommandResult(context.Document.RemoveItem(Layer), []);
    }
}

public sealed partial record MoveLayer
{
    /// Reorders a background layer within background, keeping it selected. Index is its final position.
    public MoveLayer(YamlPath Layer, int Index) : this(Layer, null, Index)
    {
        IsBackgroundMove = true;
    }

    bool IsBackgroundMove { get; init; }

    CommandResult MoveBackground(CommandContext context)
    {
        BackgroundEdits.RequireEditable(context.Document, Widget);
        var document = context.Document.MoveItem(Widget, BackgroundEdits.List, Index, out var moved);
        return new CommandResult(document, [moved]);
    }
}

/// Replaces the layer's content and kind-specific settings in one step, leaving common keys as written.
public sealed record ChangeLayerKind(YamlPath Layer, LayerKindChoice Kind, string? File) : EditorCommand
{
    public override string Label => "Change layer kind";

    public override CommandResult Run(CommandContext context)
    {
        var document = context.Document;
        BackgroundEdits.RequireEditable(document, Layer);
        var (key, value) = BackgroundEdits.Content(Kind, File);
        // BackgroundParser.Kinds and KindOnly: reset kind-specific settings even between image and video.
        string[] contentKeys = ["fill", "image", "video", "shader", "fit", "uniforms", "scale"];
        foreach (var oldKey in contentKeys)
            if (document.Exists(Layer.Child(oldKey))) document = document.RemoveKey(Layer.Child(oldKey));
        return document.AddKey(Layer, key, value);
    }
}

static class BackgroundEdits
{
    internal static readonly YamlPath List = YamlPath.Root.Child("background");

    internal static void RequireEditable(LayoutDocument document, YamlPath layer)
    {
        if (layer.Parent != List || layer.Last is not { IsKey: false } || !document.Exists(layer))
            throw new ArgumentException($"There is no background layer at {layer}.", nameof(layer));
        if (document.TryGetScalar(layer.Child("locked"), out var value) && bool.TryParse(value, out var locked) && locked)
            throw new LayoutEditException($"{layer} is locked. Unlock it before editing it.");
        if (!document.IsEditable(layer))
            throw new LayoutEditException($"{layer} uses an anchor or alias and can't be edited here.");
    }

    internal static (string Key, string Value) Content(LayerKindChoice kind, string? file)
    {
        var key = kind switch
        {
            LayerKindChoice.Fill => "fill",
            LayerKindChoice.Image => "image",
            LayerKindChoice.Video => "video",
            LayerKindChoice.Shader => "shader",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (kind == LayerKindChoice.Fill) return (key, "\"#000000\"");
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        return (key, YamlWriter.Scalar(file, inFlow: true));
    }
}
