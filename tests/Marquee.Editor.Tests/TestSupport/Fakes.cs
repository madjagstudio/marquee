using Marquee.Core.Sensors;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Session;
using Marquee.Host.SensorFeed;

namespace Marquee.Editor.Tests.TestSupport;

/// No tray: every read of the feed says it isn't running.
sealed class NoTray : ITrayFeed
{
    public Task<SensorFeedResult<SensorFeedValues>> ReadValuesAsync(TimeSpan timeout, CancellationToken cancel = default) =>
        Task.FromResult(new SensorFeedResult<SensorFeedValues>(SensorFeedStatus.NotRunning, null));

    public Task<SensorFeedResult<SensorFeedHistory>> ReadHistoryAsync(TimeSpan timeout, CancellationToken cancel = default) =>
        Task.FromResult(new SensorFeedResult<SensorFeedHistory>(SensorFeedStatus.NotRunning, null));
}

/// No sensors: nothing is ever read, so the canvas draws samples.
sealed class NoReadings : ILocalReadings
{
    readonly SensorStore _store = new(() => DateTimeOffset.UtcNow);

    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    public SensorSnapshot Snapshot() => _store.Snapshot();

    public void Dispose() => Stop();
}

/// Dialogs that answer as the test says, and remember what they were asked.
sealed class FakeDialogs : IEditorDialogs
{
    public string? StyleNameAnswer { get; set; }
    public List<string?> StyleNameChecks { get; } = [];
    public string? AskStyleName(string suggested, Func<string, string?> problem)
    {
        StyleNameChecks.Add(problem(suggested));
        if (StyleNameAnswer is { } name) StyleNameChecks.Add(problem(name));
        return StyleNameAnswer;
    }

    public bool? RearrangeAnswer { get; set; }
    public List<string> RearrangeQuestions { get; } = [];
    public Dictionary<LayerKindChoice, string?> LayerFileAnswers { get; } = [];
    public string? PickLayerFile(LayerKindChoice kind) => LayerFileAnswers.GetValueOrDefault(kind);
    public bool? AskRearrange(string orientation)
    {
        RearrangeQuestions.Add(orientation);
        return RearrangeAnswer;
    }

    public List<string> Errors { get; } = [];
    public List<string> CopyQuestions { get; } = [];
    public List<RecoveryOffer> RestoreOffers { get; } = [];
    public List<string> AlreadyOpen { get; } = [];
    public List<string> CloseQuestions { get; } = [];
    public List<string> ReplaceQuestions { get; } = [];

    public CopyChoice CopyAnswer { get; set; } = CopyChoice.Cancel;
    public bool RestoreAnswer { get; set; }
    public bool CloseAnswer { get; set; } = true;
    public bool ReplaceAnswer { get; set; }
    public string? FolderAnswer { get; set; }
    public int FolderQuestions { get; private set; }

    public List<string> Infos { get; } = [];

    /// The file each import question answers, by kind; null cancels.
    public Dictionary<AssetKind, string?> ImportAnswers { get; } = [];

    /// The copy's name to give; null cancels. CopyNameChecks keeps what the check said of it.
    public string? CopyNameAnswer { get; set; }
    public List<string?> CopyNameChecks { get; } = [];

    /// Which of the unused files offered to tick; null cancels. UnusedOffered keeps what was offered.
    public Func<IReadOnlyList<UnusedFile>, IReadOnlyList<string>?> UnusedAnswer { get; set; } = _ => null;
    public List<IReadOnlyList<UnusedFile>> UnusedOffered { get; } = [];

    /// Whether "Revert to opened version" goes ahead; RevertQuestions keeps the themes it was asked about.
    public bool RevertAnswer { get; set; } = true;
    public List<string> RevertQuestions { get; } = [];

    public void ShowError(string message) => Errors.Add(message);

    public void ShowInfo(string message) => Infos.Add(message);

    public string? PickImportFile(AssetKind kind) => ImportAnswers.GetValueOrDefault(kind);

    public string? AskCopyName(string example, string suggested, Func<string, string?> check)
    {
        CopyNameChecks.Add(check(suggested));
        if (CopyNameAnswer is { } name) CopyNameChecks.Add(check(name));
        return CopyNameAnswer;
    }

    public IReadOnlyList<string>? ChooseUnusedFiles(string theme, IReadOnlyList<UnusedFile> files)
    {
        UnusedOffered.Add(files);
        return UnusedAnswer(files);
    }

    public bool AskRevert(string theme)
    {
        RevertQuestions.Add(theme);
        return RevertAnswer;
    }

    public CopyChoice AskMakeCopy(string theme, string reason)
    {
        CopyQuestions.Add(theme);
        return CopyAnswer;
    }

    public bool AskRestore(string theme, RecoveryOffer offer)
    {
        RestoreOffers.Add(offer);
        return RestoreAnswer;
    }

    public void ShowAlreadyOpen(string theme) => AlreadyOpen.Add(theme);

    public string? PickSaveFolder()
    {
        FolderQuestions++;
        return FolderAnswer;
    }

    public bool AskCloseUnsaved(string reason)
    {
        CloseQuestions.Add(reason);
        return CloseAnswer;
    }

    public bool AskReplaceLayout(string folder)
    {
        ReplaceQuestions.Add(folder);
        return ReplaceAnswer;
    }
}

/// A clipboard of the test's own.
sealed class FakeClipboard : IClipboardText
{
    public string? Text { get; set; }

    public string? GetText() => Text;

    public bool SetText(string text)
    {
        Text = text;
        return true;
    }
}
