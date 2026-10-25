using Marquee.Core.Expressions;
using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// The clocks inside a group, such as the Clock and date module's: its text widgets, at any depth, whose text is only
/// the time and date (TextTemplate.IsClock). The group's panel sets their time or date format together, so a clock can
/// be made 12-hour without selecting the text inside it.
public static class GroupClocks
{
    /// The paths of the clock texts under `group`, in the file's order; none for a widget that isn't a group.
    public static IReadOnlyList<YamlPath> In(LayoutDocument document, YamlPath group)
    {
        var clocks = new List<YamlPath>();
        Add(document, group, clocks);
        return clocks;
    }

    /// Whether any of the clocks shows the time (or the date).
    public static bool Has(LayoutDocument document, IReadOnlyList<YamlPath> clocks, TokenKind kind) =>
        clocks.Any(clock => Parts(document, clock).OfType<TemplatePart.Token>().Any(token => token.Kind == kind));

    /// The format the clocks' time (or date) parts share, with a part that sets none as the helper's default; null
    /// when none has that part or they differ.
    public static string? Format(LayoutDocument document, IReadOnlyList<YamlPath> clocks, TokenKind kind)
    {
        var formats = clocks
            .SelectMany(clock => Parts(document, clock).OfType<TemplatePart.Token>().Where(token => token.Kind == kind))
            .Select(token => token.Format ?? (kind == TokenKind.Time ? Formatting.DefaultTimePattern : "d"))
            .Distinct()
            .ToList();
        return formats.Count == 1 ? formats[0] : null;
    }

    /// The clocks with each time (or date) part written in `pattern`, as the text box's own Time format choice writes it.
    public static LayoutDocument Change(LayoutDocument document, IReadOnlyList<YamlPath> clocks, TokenKind kind, string pattern)
    {
        foreach (var clock in clocks)
        {
            if (!document.TryGetScalar(clock.Child("text"), out var text)) continue;
            var changed = TextTemplate.ChangeFormat(text, kind, pattern);
            if (changed != text) document = document.SetScalar(clock.Child("text"), changed);
        }

        return document;
    }

    static void Add(LayoutDocument document, YamlPath group, List<YamlPath> clocks)
    {
        var children = group.Child("children");
        if (!document.TryGetScalar(group.Child("type"), out var type) || type != "group"
            || document.GetNode(children) is not { Kind: LayoutNodeKind.List } list) return;
        for (var i = 0; i < list.Count; i++)
        {
            var child = children.Item(i);
            if (document.TryGetScalar(child.Child("type"), out var childType) && childType == "text"
                && document.TryGetScalar(child.Child("text"), out var text) && IsClock(text))
                clocks.Add(child);
            else Add(document, child, clocks);
        }
    }

    static bool IsClock(string text)
    {
        try { return TextTemplate.IsClock(text); }
        catch (ExpressionException) { return false; }
    }

    static IReadOnlyList<TemplatePart> Parts(LayoutDocument document, YamlPath clock)
    {
        if (!document.TryGetScalar(clock.Child("text"), out var text)) return [];
        try { return TextTemplate.Parse(text); }
        catch (ExpressionException) { return []; }
    }
}
