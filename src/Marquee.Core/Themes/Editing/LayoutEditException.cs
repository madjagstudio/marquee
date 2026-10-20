namespace Marquee.Core.Themes.Editing;

/// A layout.yaml the editor can't open, or an edit it won't make. The document it was asked of is unchanged. Line is
/// 1-based within layout.yaml, or 0 when the problem isn't on one line; then the message is the reason alone.
///
/// An edit refused because the editor's own splicing failed (it misread the file's shape), rather than because of the
/// file, has that failure as its InnerException, so the editor can log it as a bug while the user is told to edit the
/// file by hand.
public sealed class LayoutEditException(string reason, int line = 0, Exception? inner = null)
    : Exception(line > 0 ? $"{ThemeLoader.LayoutFileName} line {line}: {reason}" : reason, inner)
{
    public string Reason { get; } = reason;
    public int Line { get; } = line;
}
