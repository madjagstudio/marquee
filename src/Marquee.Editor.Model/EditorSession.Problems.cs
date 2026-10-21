using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Themes.Validation;

namespace Marquee.Editor.Model;

/// The problems list: the text's errors and warnings, worked out again as each text is shown.
public sealed partial class EditorSession
{
    /// What `marquee validate` would say of layout.yaml as it is now, without reading the folder again: every error in
    /// the text (LayoutParser's collecting mode), not only LoadError's first, when it doesn't load; the loader's warnings
    /// when it does. Errors come in line order, then warnings. Empty for a clean theme. Changed (Theme) says when it
    /// changes. The file checks only a shared theme needs (sizes, links) are left to `marquee validate`.
    public IReadOnlyList<LayoutProblem> Problems { get; private set; } = [];

    /// The problems with `document`, which `loaded` was loaded from, or which didn't load with `error`.
    static IReadOnlyList<LayoutProblem> ProblemsOf(LayoutDocument document, Loaded? loaded, LoadError? error)
    {
        if (loaded is not null)
            return loaded.Theme.Warnings.Select(warning => new LayoutProblem(IssueSeverity.Warning, warning, 0, null, null)).ToList();

        var errors = new List<ThemeException>();
        var text = document.Text.StartsWith('\uFEFF') ? document.Text[1..] : document.Text;
        LayoutParser.Parse(text, errors);
        // The load can fail past the parser too (a theme's assets), with the one error it gave.
        var found = errors.Count > 0 ? errors.Select(e => (e.Reason, e.Line)) : error is null ? [] : [(error.Reason, error.Line)];
        return found.Select(e => Problem(document, e.Reason, e.Line)).ToList();
    }

    static LayoutProblem Problem(LayoutDocument document, string message, int line)
    {
        var path = line > 0 ? document.PathAt(line) : null;
        return new LayoutProblem(IssueSeverity.Error, message, line, path, path is null ? null : WidgetOf(path));
    }

    /// The widget `path` is in, or is: its longest start that is an item of `widgets` or of a group's `children`.
    static YamlPath? WidgetOf(YamlPath path)
    {
        var segments = path.Segments;
        YamlPath? widget = null;
        var at = YamlPath.Root;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.Key is { } key)
            {
                at = at.Child(key);
                continue;
            }

            at = at.Item(segment.Index);
            var list = segments[i - 1].Key;
            if ((list == "widgets" && i == 1) || (list == "children" && widget is not null)) widget = at;
            else break;
        }

        return widget;
    }
}
