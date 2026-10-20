using Marquee.App.Resources;
using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Host;

namespace Marquee.App;

/// The tray's side of the L-Connect handover. The engine says when the screen comes up busy (once per busy episode) and
/// what `lconnect.handover` makes of it; with Ask, a balloon offers to take the screen over, and clicking it does. A
/// user who hasn't set `lconnect.handover` gets that balloon once too, the first time L-Connect holds the screen, as the
/// handover's opt-in (there is no installer page for it). While L-Connect holds the screen and the tray could take it,
/// the menu offers "Take over" whatever the mode; "Always take over at start" switches config.yaml between `auto` and
/// `ask`. Both need an elevated tray, so an unelevated one shows neither, and a line saying why instead. Auto's own
/// tries, holding back after one fails and trying again 1, 2, 5, then every 10 minutes later, are the engine's.
sealed partial class TrayModel
{
    /// The busy episode the engine last reported, until the screen opens or a handover takes it. Written on engine
    /// threads, read when the menu opens.
    volatile ScreenBusyInfo? _busy;

    /// Why the last handover failed, for a menu line, until Marquee gets the screen.
    volatile string? _handoverFailure;

    /// The first-run hint's marker was tried in this run, so later openings (after each pause, say) don't touch the disk.
    volatile bool _hintTried;

    /// L-Connect's service holds the screen, and the tray is trying to show something on it.
    bool LConnectHasScreen => !_engine.IsPaused && _busy is { LConnect: LConnectState.Service };

    /// Takes the screen from L-Connect, on the worker. When it doesn't start (one is under way, the engine is paused,
    /// or it has the screen already) nothing needs saying: the engine has logged anything worth knowing.
    public Task TakeOverScreen() => Run(() => _engine.TakeOverScreen());

    /// "Always take over at start": writes `lconnect.handover: auto` to config.yaml, or `ask` when unchecked, keeping
    /// the rest of the file as it is. The config watcher sees the write and applies it, so the item's check mark
    /// follows the file, as it does after an edit by hand. Checked while L-Connect has the screen and it can be taken,
    /// it also takes it now: the engine reported this busy episode under the old mode, so Auto alone would wait for the
    /// next one. Not when config.yaml can't be written, since then nothing was chosen.
    public Task SetAlwaysTakeOver(bool always) => Run(() =>
    {
        ConfigEditor.SetInFile(_paths.ConfigFile, "lconnect.handover", always ? "auto" : "ask");
        if (always && LConnectHasScreen && _busy is { CanTakeOver: true }) _engine.TakeOverScreen();
    });

    /// The lines under the status: why the last handover failed, and why an unelevated tray offers no take-over.
    IEnumerable<TrayMenuItem> HandoverProblems()
    {
        if (_handoverFailure is { } failure)
            yield return new TrayMenuItem(MenuKeys.Problem, MenuText(Strings.HandoverFailedProblem(failure))) { Enabled = false };
        if (LConnectHasScreen && !_engine.IsElevated)
            yield return new TrayMenuItem(MenuKeys.Problem, Strings.TakeOverNeedsAdministrator) { Enabled = false };
    }

    /// "Take over" while L-Connect has the screen and it can be taken, then "Always take over at start" where L-Connect
    /// is installed (or the item is checked, so it can be turned off); neither when the tray isn't elevated, since
    /// neither could work.
    IEnumerable<TrayMenuItem> HandoverItems(EngineSettings? settings)
    {
        if (!_engine.IsElevated) yield break;

        if (LConnectHasScreen && _busy is { CanTakeOver: true })
            yield return new TrayMenuItem(MenuKeys.TakeOver, Strings.MenuTakeOver) { Command = () => _ = TakeOverScreen() };

        var always = settings?.Handover == HandoverMode.Auto;
        if (!always && !_engine.LConnectInstalled) yield break;
        yield return new TrayMenuItem(MenuKeys.AlwaysTakeOver, Strings.MenuAlwaysTakeOver)
        {
            Checked = always,
            Command = () => _ = SetAlwaysTakeOver(!always),
        };
    }

    /// On the engine's thread. Ask's prompt shows once per busy episode, because the engine reports each once; its
    /// click, and only its, takes over. With the handover off by default rather than by choice, the same prompt shows
    /// once per user (FirstRun.ClaimHandoverOffer), the first time it could work, and then also names "Always take over
    /// at start", since its click takes over only this once.
    void OnScreenBusy(ScreenBusyInfo info)
    {
        _busy = info;
        if (!info.CanTakeOver) return;
        string? prompt = null;
        if (info.Action == HandoverAction.Prompt)
            prompt = Strings.TakeOverPrompt;
        else if (info.Action == HandoverAction.None && !_engine.HandoverChosen && FirstRun.ClaimHandoverOffer(_paths.DataFolder))
            prompt = Strings.FirstRunTakeOverPrompt(Strings.TakeOverPrompt, Strings.MenuAlwaysTakeOver);
        if (prompt is not null)
            _host.ShowBalloon(Strings.TakeOverTitle, prompt, isError: false, onClick: () => _ = TakeOverScreen());
    }

    /// On the handover's thread. Success is quiet, since the picture on the screen says it; so is a handover cut short
    /// by Pause or Quit, which didn't fail. A failure is a menu line until Marquee gets the screen, and a balloon, at
    /// most once a minute (its own minute, so no other error hides it), as Auto tries again (a minute later at the
    /// soonest). With Auto the balloon says so.
    void OnHandoverFinished(HandoverResult result)
    {
        switch (result.Outcome)
        {
            case HandoverOutcome.Taken:
                _busy = null;
                _handoverFailure = null;
                break;
            case HandoverOutcome.Failed:
                _handoverFailure = result.Message;
                Warn(Strings.HandoverFailedTitle, _engine.Settings?.Handover == HandoverMode.Auto
                    ? Strings.HandoverFailedRetrying(result.Message)
                    : Strings.HandoverFailed(result.Message));
                break;
        }
    }

    /// On an engine thread, after the ScreenBusy of the episode it ends, however it ended (a handover, or L-Connect
    /// stopped by hand), so a late ScreenBusy can't bring back a stale "Take over".
    ///
    /// On the first run (config.yaml as last read names no theme and has no error, and the hint hasn't been shown) a
    /// balloon then says where to pick a theme and the orientation. Not at Start: the engine reports a busy screen only
    /// after its first frame, so at Start nothing yet says whether L-Connect has the screen, and the hint would race the
    /// handover's offer and use itself up unseen. Here Marquee's picture is on the screen, the offer (if any) came first,
    /// and the hint, which does nothing when clicked, replaces it as the balloon showing, so its click can't take over.
    void OnScreenOpened()
    {
        _busy = null;
        _handoverFailure = null;
        if (_hintTried || _engine.IsPaused || !_engine.UsesDefaultTheme) return;
        _hintTried = true;
        if (FirstRun.ClaimHint(_paths.DataFolder)) _host.ShowBalloon(Strings.FirstRunHintTitle, Strings.FirstRunHint, isError: false);
    }
}
