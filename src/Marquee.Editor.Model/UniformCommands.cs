using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Binds one shader uniform, creating the mapping if needed, as one undo step.
public sealed record SetUniform(YamlPath Layer, string Name, IReadOnlyList<string> Values) : EditorCommand
{
    public override string Label => "Set uniform";

    public override CommandResult Run(CommandContext context)
    {
        BackgroundEdits.RequireEditable(context.Document, Layer);
        foreach (var component in Values)
            if (ShaderLayerModel.Check(component) is { } problem)
                throw new ArgumentException(problem, nameof(Values));

        var components = Values.Select(component => ShaderLayerModel.IsNumber(component)
            ? component : YamlWriter.Scalar(component, inFlow: true, forceQuotes: true)).ToArray();
        var yaml = components.Length == 1 ? components[0] : $"[{string.Join(", ", components)}]";
        var document = context.Document;
        var uniforms = Layer.Child("uniforms");
        var binding = uniforms.Child(Name);
        if (document.Exists(binding)) return document.ReplaceValue(binding, yaml);
        if (!document.Exists(uniforms)) document = document.AddKey(Layer, "uniforms", "{ }");
        return document.AddKey(uniforms, Name, yaml);
    }
}

/// Removes a binding, leaving any declared uniform unbound (zero).
public sealed record RemoveUniform(YamlPath Layer, string Name) : EditorCommand
{
    public override string Label => "Remove uniform";

    public override CommandResult Run(CommandContext context)
    {
        BackgroundEdits.RequireEditable(context.Document, Layer);
        return context.Document.RemoveKey(Layer.Child("uniforms").Child(Name));
    }
}
