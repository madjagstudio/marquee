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

    public void ShowError(string message) => Errors.Add(message);

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
