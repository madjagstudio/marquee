using System.Globalization;
using Marquee.Core.Themes.Validation;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Shell;

/// What the window says about the open theme: whether it is saved, its problems, the banner over the canvas while
/// editing is paused or the theme is read-only, whether crash recovery is working, and whether the tray is running.
public sealed partial class MainViewModel
{
    string _saveText = "";
    string? _saveDetail;
    bool _canSaveToFolder;
    IReadOnlyList<LayoutProblem> _problems = [];
    string? _banner;
    string? _recoveryProblem;
    bool _trayNotRunning;

    /// The pause the user put out of the way ("Keep editing in your text editor"): the banner stays hidden until the
    /// pause ends or its error changes.
    LoadError? _dismissedPause;

    /// Saved, Saving… or Not saved, as the toolbar shows it.
    public string SaveText
    {
        get => _saveText;
        private set => Set(ref _saveText, value);
    }

    /// Why the last save failed and when it is tried again; null unless it failed.
    public string? SaveDetail
    {
        get => _saveDetail;
        private set
        {
            if (Set(ref _saveDetail, value)) Raise(nameof(SaveFailed));
        }
    }

    public bool SaveFailed => SaveDetail is not null;

    /// The theme's folder is gone, so the save-state menu offers "Save to a folder…" (SaveToFolderCommand).
    public bool CanSaveToFolder
    {
        get => _canSaveToFolder;
        private set => Set(ref _canSaveToFolder, value);
    }

    /// The text's errors and warnings (EditorSession.Problems), for the toolbar's problems list.
    public IReadOnlyList<LayoutProblem> Problems
    {
        get => _problems;
        private set
        {
            if (Set(ref _problems, value))
            {
                Raise(nameof(ProblemsText));
                Raise(nameof(HasProblems));
            }
        }
    }

    public bool HasProblems => Problems.Count > 0;

    public string ProblemsText => Problems.Count switch
    {
        0 => Strings.NoProblems,
        1 => Strings.OneProblem,
        _ => Strings.Problems(Problems.Count),
    };

    /// What the bar over the canvas says: editing paused on a file that doesn't load, or a read-only theme. Null when
    /// there is no bar.
    public string? Banner
    {
        get => _banner;
        private set
        {
            if (Set(ref _banner, value)) Raise(nameof(HasBanner));
        }
    }

    public bool HasBanner => Banner is not null;

    /// The banner offers "Revert to opened version": editing is paused, and the theme can be saved.
    public bool CanRevert => _workspace is { Backup: not null, Session.IsPaused: true };

    /// The banner offers "Keep editing in your text editor", which puts it away: editing is paused.
    public bool CanKeepEditing => _workspace is { Session.IsPaused: true };

    /// The banner offers "Make a copy to edit": the theme is read-only.
    public bool CanMakeCopy => _workspace is { IsReadOnly: true };

    /// Why crash recovery isn't keeping the text, for the status bar; null while it is.
    public string? RecoveryProblem
    {
        get => _recoveryProblem;
        private set => Set(ref _recoveryProblem, value);
    }

    /// No Marquee of this user serves the sensor feed, so the canvas shows the editor's own readings and samples.
    public bool TrayNotRunning
    {
        get => _trayNotRunning;
        private set => Set(ref _trayNotRunning, value);
    }

    public string UndoToolTip => _workspace?.Session.History.NextUndo is { } step ? Strings.UndoStep(step.Label) : Strings.Undo;

    public string RedoToolTip => _workspace?.Session.History.NextRedo is { } step ? Strings.RedoStep(step.Label) : Strings.Redo;

    void RefreshStatus()
    {
        RefreshSaveState();
        RefreshTray();
        RefreshRecovery();
        if (_workspace is not { } workspace)
        {
            Problems = [];
            Banner = null;
            return;
        }

        var session = workspace.Session;
        // Paused on a file that isn't YAML, the session's Problems are its own last text's; what is wrong is the file's.
        IReadOnlyList<LayoutProblem> problems = session is { IsFileUnreadable: true, LoadError: { } unreadable }
            ? [new LayoutProblem(IssueSeverity.Error, unreadable.Reason, unreadable.Line, null, null), .. session.Problems]
            : session.Problems;
        if (!problems.SequenceEqual(Problems)) Problems = problems;
        if (!session.IsPaused) _dismissedPause = null;

        Banner = session.LoadError is { } paused && session.IsPaused && paused != _dismissedPause
            ? Strings.PausedBanner(paused.ToString())
            : workspace.IsReadOnly ? Strings.ReadOnlyBanner(workspace.Access.Reason ?? "")
            : null;
        Raise(nameof(CanRevert));
        Raise(nameof(CanKeepEditing));
        Raise(nameof(CanMakeCopy));
        Raise(nameof(UndoToolTip));
        Raise(nameof(RedoToolTip));
        RefreshCommands();
    }

    /// "Keep editing in your text editor": the banner goes; editing stays paused until the file loads, or a revert.
    void KeepEditing()
    {
        _dismissedPause = _workspace?.Session.LoadError;
        RefreshStatus();
    }

    void RefreshSaveState()
    {
        if (_workspace is not { } workspace)
        {
            SaveText = "";
            SaveDetail = null;
            CanSaveToFolder = false;
            return;
        }

        if (workspace.Autosaver is not { } autosaver)
        {
            SaveText = Strings.SaveReadOnly;
            SaveDetail = null;
            CanSaveToFolder = false;
            return;
        }

        var state = autosaver.State;
        SaveText = state.Status switch
        {
            SaveStatus.Saved => Strings.Saved,
            SaveStatus.Saving => Strings.Saving,
            _ => Strings.NotSaved,
        };
        SaveDetail = state.Status == SaveStatus.Failed
            ? Strings.SaveFailedDetail(state.Reason ?? "", When(state.RetryAt))
            : null;
        CanSaveToFolder = state.Problem == SaveProblem.ThemeGone;
        SaveToFolderCommand.Refresh();
    }

    void RefreshRecovery() => RecoveryProblem = _workspace?.RecoveryProblem is { } problem ? Strings.RecoveryOff(problem) : null;

    void RefreshTray() => TrayNotRunning = _workspace?.Values.TrayNotRunning ?? false;

    static string When(DateTimeOffset? time) =>
        time is { } at ? at.ToLocalTime().ToString("T", CultureInfo.CurrentCulture) : "";
}
