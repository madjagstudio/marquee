using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Resources;
using Marquee.Editor.Startup;

namespace Marquee.Editor.Session;

/// One theme open in a window: the session, and what saves it and keeps it safe, made in the order they rely on each
/// other (Open):
/// 1. the theme folder's lock (one editor per theme); when another editor has it, that editor's window is brought
///    forward (WindowSignal) and this one opens nothing, asking nothing;
/// 2. whether it can be saved (ThemeOpening.Check), with a copy to edit offered when it can't: the copy's lock is taken
///    in place of the theme's;
/// 3. the session (EditorSession.Open);
/// 4. saving: the file, the opened-version backup and autosave;
/// 5. crash recovery, whose restore offer is put before the first change;
/// 6. the watch for hand edits, which looks once at once;
/// 7. the value sources, attached to the session.
/// Close takes them apart in the order the model documents for shutdown (Close).
///
/// A theme opened read-only (an installed example the user chose to look at) gets no saving and no recovery: nothing
/// is written, and the shell turns editing off.
///
/// Built and used on the UI thread, whose context the parts post back to.
public sealed class EditorWorkspace : IDisposable
{
    readonly ThemeLock _lock;
    readonly RecoveryStore? _recovery;
    readonly ExternalChangeWatcher _watcher;
    readonly WindowSignal? _signal;
    bool _closed;

    EditorWorkspace(EditorSession session, ThemeAccess access, ThemeLock themeLock, EditorEnvironment environment,
        SynchronizationContext ui)
    {
        Session = session;
        Access = access;
        _lock = themeLock;
        var made = new List<IDisposable>();
        try
        {
            var file = LayoutFile.For(session);
            if (!access.IsReadOnly)
            {
                Backup = OpenedBackup.For(session, environment.Folders);
                Autosaver = new Autosaver(session, file, Backup, ui, environment.Time);
                made.Add(Autosaver);
                _recovery = new RecoveryStore(session, environment.Folders, ui, environment.Time);
                _recovery.ProblemChanged += (_, _) => RecoveryProblemChanged?.Invoke(this, EventArgs.Empty);
                made.Add(_recovery);
                if (_recovery.Offer is { } offer && environment.Dialogs.AskRestore(Name, offer))
                    Restore(offer, environment.Dialogs);
            }

            _watcher = new ExternalChangeWatcher(session, file, ui, environment.Time);
            made.Add(_watcher);
            _watcher.Start();

            Values = environment.ValueSources();
            made.Add(Values);
            Values.Attach(session);

            _signal = WindowSignal.Listen(session.Folder, ui, () => ShowRequested?.Invoke(this, EventArgs.Empty));
        }
        catch
        {
            // Without CloseCleanly: a recovery copy already there stays, to be offered again.
            made.Reverse();
            foreach (var part in made) part.Dispose();
            throw;
        }
    }

    public EditorSession Session { get; }

    /// Whether the theme can be saved, and if not, why.
    public ThemeAccess Access { get; }

    public bool IsReadOnly => Access.IsReadOnly;

    /// The theme folder's name.
    public string Name => Path.GetFileName(Session.Folder);

    /// Null when the theme is read-only.
    public Autosaver? Autosaver { get; }

    /// "Revert to opened version"; null when the theme is read-only.
    public OpenedBackup? Backup { get; }

    public ValueSources Values { get; }

    /// Why crash recovery isn't keeping the text (RecoveryStore.Problem), for the status bar; null while all is well, and
    /// for a read-only theme.
    public string? RecoveryProblem => _recovery?.Problem;

    /// Raised on the UI thread when another editor asked for this theme: the window comes forward.
    public event EventHandler? ShowRequested;

    /// Raised on the UI thread when RecoveryProblem changes.
    public event EventHandler? RecoveryProblemChanged;

    /// Opens the theme in `themeFolder`, asking what the user wants where the theme needs it. Null when nothing was
    /// opened: the user declined, another editor has the theme (and was brought forward), or it couldn't be opened, which
    /// the dialogs have said.
    public static EditorWorkspace? Open(string themeFolder, EditorEnvironment environment, SynchronizationContext ui)
    {
        var dialogs = environment.Dialogs;
        var folder = ThemeIdentity.FullPath(themeFolder);
        // The lock first: a theme another editor has open brings that window forward, without asking anything here.
        if (TryLock(folder, dialogs) is not { } themeLock) return null;
        var access = environment.Opening.Check(folder);
        if (access.IsReadOnly)
        {
            switch (dialogs.AskMakeCopy(Path.GetFileName(folder), access.Reason!))
            {
                case CopyChoice.Cancel:
                    themeLock.Dispose();
                    return null;
                case CopyChoice.MakeCopy:
                    var copy = MakeCopy(folder, environment);
                    themeLock.Dispose();
                    // A new folder, so no editor has it, unless one opened it in the moment since.
                    if (copy is null || TryLock(copy, dialogs) is not { } copyLock) return null;
                    (folder, themeLock) = (copy, copyLock);
                    access = environment.Opening.Check(folder);
                    break;
            }
        }

        try
        {
            var session = EditorSession.Open(folder, new EditorSessionOptions { Time = environment.Time });
            try
            {
                return new EditorWorkspace(session, access, themeLock, environment, ui);
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is ThemeException or LayoutEditException or IOException or UnauthorizedAccessException)
        {
            themeLock.Dispose();
            dialogs.ShowError(Strings.CouldNotOpen(Path.GetFileName(folder), ex.Message));
            return null;
        }
        catch
        {
            themeLock.Dispose();
            throw;
        }
    }

    /// Takes `folder`'s lock (ThemeOpening.TryLock); null when another editor has it, whose window is brought forward,
    /// or, when it can't be, the dialogs say so.
    static ThemeLock? TryLock(string folder, IEditorDialogs dialogs)
    {
        if (ThemeOpening.TryLock(folder) is { } themeLock) return themeLock;
        if (!WindowSignal.Send(folder)) dialogs.ShowAlreadyOpen(Path.GetFileName(folder));
        return null;
    }

    /// Copies `themeFolder` into the user's themes (ThemeOpening.MakeCopy) for editing; null when it couldn't, which
    /// the dialogs have said.
    public static string? MakeCopy(string themeFolder, EditorEnvironment environment)
    {
        try
        {
            return environment.Opening.MakeCopy(themeFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            environment.Dialogs.ShowError(Strings.CouldNotCopy(Path.GetFileName(themeFolder), ex.Message));
            return null;
        }
    }

    /// Saves what waits now, for a clean close. True when layout.yaml holds the session's text; false when the save
    /// failed or didn't finish within Autosaver.FlushTimeout, and Autosaver.State says why.
    public bool Flush() => Autosaver?.Flush() ?? true;

    /// For "Save to a folder…" when the theme's folder is gone (SaveProblem.ThemeGone): writes the text as layout.yaml in
    /// `folder` (Autosaver.SaveTo) and returns the file. A layout.yaml already there is replaced only when `replace`:
    /// otherwise nothing is written and it returns null, for the shell to ask first. Throws as SaveTo does, and nothing
    /// changes.
    public string? SaveTo(string folder, bool replace = false) =>
        (Autosaver ?? throw new InvalidOperationException("A read-only theme isn't saved.")).SaveTo(folder, replace);

    /// Closes the theme in the order the model's parts rely on (watcher, value sources, autosave after a last save,
    /// recovery, session, lock): saves what waits, then deletes the recovery copy, or keeps it when the save failed so
    /// the changes are offered next time. True when everything was saved. Each wait is bounded (Autosaver.FlushTimeout,
    /// ValueSources.CloseWait), so a stuck disk or sensor never holds the window for long.
    public bool Close()
    {
        if (_closed) return true;
        _closed = true;
        // First, so the last save isn't read back as a change from outside, and nothing it posts reaches a closed session.
        _watcher.Dispose();
        Values.Dispose();
        var saved = Flush();
        Autosaver?.Dispose();
        if (saved) _recovery?.CloseCleanly();
        else _recovery?.Dispose();
        _signal?.Dispose();
        Session.Dispose();
        _lock.Dispose();
        return saved;
    }

    /// Closes without saving what waits: for a failure, not a close. The recovery copy stays.
    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _watcher.Dispose();
        Values.Dispose();
        Autosaver?.Dispose();
        _recovery?.Dispose();
        _signal?.Dispose();
        Session.Dispose();
        _lock.Dispose();
    }

    void Restore(RecoveryOffer offer, IEditorDialogs dialogs)
    {
        try
        {
            Session.Apply(offer.Restore());
        }
        catch (Exception ex) when (ex is LayoutEditException or ThemeException or IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError(Strings.CouldNotRestore(ex.Message));
        }
    }
}
