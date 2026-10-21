namespace Marquee.Editor.Model;

/// A session's layout.yaml on disk, and the bytes it holds as far as the session knows: what the editor last wrote, or
/// read when it opened, or took from outside (Accept). A file holding those bytes is as the session left it; anything
/// else is a change from outside the editor, which the watcher reloads.
///
/// Autosave never writes over such a change: Write compares the file with the last bytes, and writes only when they
/// are the same, all while the gate is held, so the external change watcher's read can't land in between. It also
/// refuses bytes made before the session last took text from outside (Taken), which would put back what that replaced.
/// Another program's save landing in the moment between Write's comparison and its File.Replace, which no comparison
/// can close, is caught after it: the replace keeps the file it displaced, and a hand edit found there is put back.
///
/// Thread-safe: autosave writes from a timer thread, the watcher reads from another, and the UI thread accepts.
public sealed class LayoutFile
{
    /// Held while comparing and replacing, reading, or accepting, so each sees the others whole.
    readonly object _gate = new();

    readonly ReparseTags _tags;

    /// Set with _gate held, and read without it, so asking never waits for a slow write.
    volatile byte[] _last;

    /// Changed with _gate held; read with Interlocked, so the UI thread never waits for a write to ask.
    long _taken;

    /// `bytes` are what the session read when it opened the file.
    public LayoutFile(string path, byte[] bytes)
        : this(path, bytes, ReparseTags.Windows)
    {
    }

    /// `tags` is ReparseTags.Windows except in tests.
    internal LayoutFile(string path, byte[] bytes, ReparseTags tags)
    {
        Path = path;
        _last = bytes;
        _tags = tags;
    }

    /// The file of `session`, as it read it: its document's bytes, which are the file's (LayoutDocument.FromBytes).
    public static LayoutFile For(EditorSession session) => new(session.LayoutPath, session.Document.ToBytes());

    public string Path { get; }

    /// How many times the session has taken text from outside (Accept). Bytes made from the session's text carry the
    /// count they were made at, and Write refuses them once it has moved on.
    public long Taken => Interlocked.Read(ref _taken);

    /// Run with the gate held, just before the replace: for tests, to land another program's save in the moment between
    /// the comparison and the replace.
    internal Action? BeforeReplace { get; set; }

    /// Raised, on the writing thread, when Write finds the file changed outside the editor and leaves it alone, so
    /// the external change watcher reads it now rather than at its next file event.
    public event EventHandler? ChangedOutside;

    /// Whether `bytes` are the last the editor wrote, read or took.
    public bool IsLast(ReadOnlySpan<byte> bytes) => bytes.SequenceEqual(_last);

    /// Writes `bytes`, made from the session's text when Taken was `taken`, unless that would write over anything the
    /// session hasn't seen:
    /// - Stale, writing nothing, when the session has taken text from outside since;
    /// - ChangedOutside, writing nothing and raising ChangedOutside, when the file doesn't hold the last bytes;
    /// - Written otherwise, atomically (AtomicFile: a temporary file in the folder, flushed, then File.Replace), and
    ///   the bytes are the last from then on.
    /// The temporary file is written first, so the gate is held only for the comparison and the replace. Another
    /// program's save landing between those two (microseconds) is displaced by the replace, which keeps it as a backup:
    /// when the backup isn't the last bytes, it is put back over what was just written, and the result is
    /// ChangedOutside, as if it had landed before the comparison. A backup is deleted only when it holds the editor's
    /// own bytes, so whatever else happens, a hand edit is never deleted: at worst it is left beside layout.yaml.
    ///
    /// A cloud placeholder on the path (OneDrive) is written through; any other reparse point, the file included, is
    /// refused (ReparseTags). Throws UnsafePathException for a link, DirectoryNotFoundException or
    /// FileNotFoundException when the folder or the file is gone (neither is made again), or the IOException or
    /// UnauthorizedAccessException of a read or write that failed; the file is as it was.
    public LayoutWrite Write(byte[] bytes, long taken)
    {
        if (_tags.FirstLink(Path) is { } link) throw ReparseTags.Refusal(Path, link);
        var before = _last;
        var temporary = AtomicFile.WriteTemporary(Path, bytes);
        var displaced = AtomicFile.TemporaryName(Path, "old");
        var putBack = AtomicFile.TemporaryName(Path, "old");
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                LayoutWrite? result = null;
                lock (_gate)
                {
                    if (taken != _taken) result = LayoutWrite.Stale;
                    else if (!Read().AsSpan().SequenceEqual(_last)) result = LayoutWrite.ChangedOutside;
                    else
                    {
                        BeforeReplace?.Invoke();
                        if (ReplaceOrUndo(temporary, displaced))
                        {
                            if (Holds(displaced, _last))
                            {
                                _last = bytes;
                                result = LayoutWrite.Written;
                            }
                            else
                            {
                                if (!ReplaceOrUndo(displaced, putBack))
                                    throw new IOException($"A hand edit to {Path} landed as the editor saved, and couldn't be put back: it is kept as {displaced}.");
                                result = LayoutWrite.ChangedOutside;
                            }
                        }
                    }
                }

                if (result == LayoutWrite.ChangedOutside) ChangedOutside?.Invoke(this, EventArgs.Empty);
                if (result is { } done) return done;
                // Another program has the file open without sharing it for deleting: wait it out, comparing again.
                if (attempt == AtomicFile.RetryDelaysMs.Length)
                    throw new IOException($"Can't replace {Path}: another program has it open.");
                Thread.Sleep(AtomicFile.RetryDelaysMs[attempt]);
            }
        }
        finally
        {
            AtomicFile.DeleteQuietly(temporary);
            DeleteIfOwn(displaced, before, bytes);
            DeleteIfOwn(putBack, before, bytes);
        }
    }

    /// The replace (AtomicFile.TryReplace, or a test's), keeping what layout.yaml held as `backup`. One that fails part
    /// way (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2) has moved layout.yaml to the backup name and left nothing at its own:
    /// it is moved back before the exception goes on.
    bool ReplaceOrUndo(string replacement, string backup)
    {
        try
        {
            return Replace(replacement, Path, backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(Path) && File.Exists(backup))
            {
                try
                {
                    File.Move(backup, Path);
                }
                catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
                {
                    // Left under the backup name, which isn't deleted (DeleteIfOwn): the user is told where.
                    throw new IOException($"Saving {Path} failed part way, and the file couldn't be put back: it is kept as {backup}.", ex);
                }
            }

            throw;
        }
    }

    /// The replace Write makes: AtomicFile.TryReplace, except in tests, which make it fail part way.
    internal Func<string, string, string, bool> Replace { get; set; } = AtomicFile.TryReplace;

    /// Deletes the backup at `path` when it holds the editor's own bytes (`before` or `written`), or isn't there. Any
    /// other file there is a hand edit, kept rather than lost.
    static void DeleteIfOwn(string path, byte[] before, byte[] written)
    {
        if (!File.Exists(path)) return;
        if (Holds(path, before) || Holds(path, written)) AtomicFile.DeleteQuietly(path);
    }

    /// Whether the file at `path` holds `bytes`. One that can't be read doesn't, so a hand edit is never taken for the
    /// editor's own bytes and deleted.
    static bool Holds(string path, byte[] bytes)
    {
        try
        {
            return File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// Writes `bytes` as the layout.yaml at `path`, another folder's, refusing a link on the path as Write does: for
    /// Autosaver.SaveTo. Made new (AtomicFile.TryWriteNew), or replaced when `replace` (AtomicFile.WriteOver); false,
    /// writing nothing, when something has the name and `replace` is false. This file's last bytes are untouched.
    public bool WriteCopy(string path, byte[] bytes, bool replace)
    {
        if (_tags.FirstLink(path) is { } link) throw ReparseTags.Refusal(path, link);
        if (!replace) return AtomicFile.TryWriteNew(path, bytes);
        AtomicFile.WriteOver(path, bytes);
        return true;
    }

    /// The file's bytes when they aren't the last the editor wrote, read or took; null when they are. They stay a
    /// change until the session takes them (Accept), so autosave doesn't write over them meanwhile. Throws IOException
    /// or UnauthorizedAccessException when it can't be read, as while another program writes it.
    public byte[]? ReadChanged()
    {
        lock (_gate)
        {
            var bytes = Read();
            return bytes.AsSpan().SequenceEqual(_last) ? null : bytes;
        }
    }

    /// The session has taken `bytes`, read from the file (ReadChanged), as its text or as the file it is paused on:
    /// they are the last bytes from now on, and Taken moves on, so bytes made before can't be written over them. On
    /// the UI thread, before the session takes them.
    public void Accept(byte[] bytes)
    {
        lock (_gate)
        {
            _last = bytes;
            Interlocked.Increment(ref _taken);
        }
    }

    /// Shared for writing and deleting, as a careful reader opens a file others replace: a program part way through
    /// writing it in place gives bytes that aren't the last, so they count as a change and are read again later.
    byte[] Read()
    {
        using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}

/// What came of LayoutFile.Write.
public enum LayoutWrite
{
    /// The file held the last bytes, and now holds the new ones.
    Written,

    /// The file holds a change from outside the editor that the session hasn't taken yet; nothing was written.
    ChangedOutside,

    /// The session has taken text from outside since the bytes were made, so they would put back what it replaced;
    /// nothing was written.
    Stale,
}
