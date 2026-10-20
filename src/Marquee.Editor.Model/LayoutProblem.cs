using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Validation;

namespace Marquee.Editor.Model;

/// One line of the toolbar's problems list, as `marquee validate` would report it for the text as it is:
/// - Severity: an error the theme can't load with, or a warning it loads despite (a missing image, a font this PC
///   hasn't);
/// - Message: what is wrong, in the parser's or the loader's words;
/// - Line: its 1-based line in layout.yaml, or 0 when it isn't on one (an asset's warning);
/// - Path: the deepest node that line is in (LayoutDocument.PathAt), such as `widgets[3].style.color`, and Widget, the
///   widget that holds it, to select; either is null when the line isn't in one.
public sealed record LayoutProblem(IssueSeverity Severity, string Message, int Line, YamlPath? Path, YamlPath? Widget)
{
    public override string ToString() => Line > 0 ? $"layout.yaml line {Line}: {Message}" : Message;
}
