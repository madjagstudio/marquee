using Marquee.Core.Files;
using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// "As it was when you opened it": the layout's bytes when the session opened, copied under the editor's root before
/// the session's first save (Autosaver calls EnsureWritten), so the file can be put back even after the editor closes.
/// Each session keeps its own: the first save of the next one replaces the copy.
///
/// Thread-safe: autosave writes it from a timer thread.
public sealed class OpenedBackup
{
    public const string RevertLabel = "Revert to opened version";

    readonly object _gate = new();
    readonly byte[] _opened;
    bool _written;

    public OpenedBackup(string path, byte[] opened)
    {
        Path = path;
        _opened = opened;
    }

    /// The backup of `session`'s file, from its document's bytes, which are the file's as it opened.
    public static OpenedBackup For(EditorSession session, EditorFolders folders) =>
        new(folders.OpenedFile(session.Folder), session.Document.ToBytes());

    /// Where the copy goes: EditorFolders.OpenedFile.
    public string Path { get; }

    /// Whether this session has written its copy.
    public bool IsWritten
    {
        get
        {
            lock (_gate) return _written;
        }
    }

    /// Writes the copy (atomically, making the folder) if this session hasn't yet. Throws IOException,
    /// UnauthorizedAccessException or UnsafePathException when it can't, and the next call tries again: a save must
    /// not go ahead without it.
    public void EnsureWritten()
    {
        lock (_gate)
        {
            if (_written) return;
            SafeFile.WriteAllBytes(Path, _opened);
            _written = true;
        }
    }

    /// The command that puts the opened text back, as one undo step. The text comes from the bytes kept in memory,
    /// which are the copy's, so it works whether or not the copy was written.
    public ReplaceDocument Revert() => new(LayoutDocument.FromBytes(_opened).Text, RevertLabel);
}
