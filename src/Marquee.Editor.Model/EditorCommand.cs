using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// One user action on the open theme. Run turns it into one or more LayoutDocument edits, made as one undo step by
/// EditorSession.Apply; it throws LayoutEditException when the file can't take it, and the session then changes
/// nothing. Commands are records, so new kinds derive from this one where they are needed.
public abstract record EditorCommand
{
    /// What undo calls the step ("Undo Move").
    public abstract string Label { get; }

    /// Commands with equal keys (compared with Equals) applied within the undo stack's merge window become one step,
    /// such as a slider's changes to one key or a run of nudges. Null, the default, never merges.
    public object? MergeKey { get; init; }

    /// True for text from outside the editor (ReplaceDocument), which the session takes even when it doesn't load as a
    /// theme: it pauses editing and shows the error. Any other command whose text doesn't load is refused.
    public virtual bool TakesBrokenTheme => false;

    /// The edits, made on context.Document, and the selection afterwards (null keeps the widgets still there).
    public abstract CommandResult Run(CommandContext context);
}

/// What a command's edits leave: the new document and, when the command chooses one, the new selection.
public sealed record CommandResult(LayoutDocument Document, IReadOnlyList<YamlPath>? Selection = null)
{
    public static implicit operator CommandResult(LayoutDocument document) => new(document);
}

/// The session as a command finds it.
public sealed class CommandContext
{
    readonly WidgetTree _tree;

    internal CommandContext(LayoutDocument document, WidgetTree tree, IReadOnlyList<YamlPath> selection, SKSizeI canvasSize)
    {
        Document = document;
        _tree = tree;
        Selection = selection;
        CanvasSize = canvasSize;
    }

    public LayoutDocument Document { get; }

    /// The top-level widgets, back to front, with their children.
    public IReadOnlyList<WidgetRef> Widgets => _tree.Roots;

    public IReadOnlyList<YamlPath> Selection { get; }

    /// The canvas in theme pixels: 1920×480 or 480×1920.
    public SKSizeI CanvasSize { get; }

    /// The widget at `path`, or null when there is none.
    public WidgetRef? Widget(YamlPath path) => _tree.Find(path);

    /// The widget at `path`; throws ArgumentException when there is none.
    public WidgetRef RequireWidget(YamlPath path) =>
        _tree.Find(path) ?? throw new ArgumentException($"There is no widget at {path}.", nameof(path));
}
