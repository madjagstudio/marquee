using System.Globalization;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

internal static class RuleCommands
{
    internal static readonly YamlPath Ring = YamlPath.Root.Child("ring");
    internal static IReadOnlyList<string> Conditions => ThemeSchema.Block("when")!.ExactlyOneOf;

    internal static YamlPath OwnerOf(LayoutDocument document, YamlPath rule)
    {
        if (rule.Last is not { IsKey: false } || rule.Parent?.Last is not { Key: "when" }
            || document.GetNode(rule) is not { Kind: LayoutNodeKind.Mapping })
            throw new PropertyValueException($"There is no when rule at {rule}.");
        return rule.Parent.Parent!;
    }

    internal static string ValueOf(string condition, string? value, string? previous = null)
    {
        if (!Conditions.Contains(condition)) throw new PropertyValueException($"Unknown rule condition '{condition}'.");
        if (condition == "stale") return "true";
        if (value is null && double.TryParse(previous, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number)) value = previous;
        return value ?? "0";
    }
}

/// Each rule edit returns one document, so creating its list or replacing its condition is one undo step.
public sealed record AddRule(YamlPath Owner, string Condition, string Value) : EditorCommand
{
    public override string Label => "Add rule";

    public override CommandResult Run(CommandContext context)
    {
        var doc = context.Document;
        if (Owner != RuleCommands.Ring) context.RequireWidget(Owner);
        var rule = YamlMap.Empty.With(Condition, RuleCommands.ValueOf(Condition, Value));
        if (Owner == RuleCommands.Ring)
        {
            if (!doc.Exists(Owner)) doc = doc.AddKey(YamlPath.Root, "ring", "{}");
            rule = rule.With("color", "#FF0000");
        }
        else
        {
            YamlValue style = StyleValues.Names(doc).FirstOrDefault() is { } name ? new YamlText(name) : YamlMap.Empty;
            rule = rule.With("style", style);
        }
        var list = Owner.Child("when");
        if (!doc.Exists(list)) doc = doc.AddKey(Owner, "when", "[]");
        return doc.InsertItem(list, doc.GetNode(list)!.Count, YamlWriter.Flow(rule));
    }
}

public sealed record RemoveRule(YamlPath Rule) : EditorCommand
{
    public override string Label => "Remove rule";

    public override CommandResult Run(CommandContext context)
    {
        RuleCommands.OwnerOf(context.Document, Rule);
        return context.Document.RemoveItem(Rule);
    }
}

public sealed record MoveRule(YamlPath Rule, int Index) : EditorCommand
{
    public override string Label => "Move rule";

    public override CommandResult Run(CommandContext context)
    {
        RuleCommands.OwnerOf(context.Document, Rule);
        return context.Document.MoveItem(Rule, Rule.Parent!, Index);
    }
}

public sealed record SetCondition(YamlPath Rule, string Condition, string? Value) : EditorCommand
{
    public override string Label => "Set rule condition";

    public override CommandResult Run(CommandContext context)
    {
        var doc = context.Document;
        RuleCommands.OwnerOf(doc, Rule);
        var old = RuleCommands.Conditions.FirstOrDefault(key => doc.Exists(Rule.Child(key)));
        var previous = old is not null && doc.TryGetScalar(Rule.Child(old), out var written) ? written : null;
        var value = RuleCommands.ValueOf(Condition, Value, previous);
        if (old is not null) doc = doc.RemoveKey(Rule.Child(old));
        return doc.AddKey(Rule, Condition, YamlWriter.Flow(new YamlText(value)));
    }
}
