using Marquee.App.Resources;
using Marquee.Host.Updates;

namespace Marquee.App;

/// The tray's side of self-update (the checking and downloading are TrayUpdates'). A newer release shows as a balloon
/// and as "Install Marquee <version>…" in the menu, which stays until the tray exits; nothing is downloaded until the
/// user clicks one. Installing downloads into the admin-only updates folder, with progress in the status line, then
/// pauses, which darkens the screen and the ring, starts the installer and waits for it, paused. The installer asks the
/// tray to quit before it replaces the tray's files, and starts it again when it finishes. An installer that exits
/// first didn't install (cancelled, another installation under way, a failure it rolled back): the screen comes back,
/// with a balloon that names the installer's exit code and log. So a failed update never leaves the screen dark.
/// Anything that goes wrong is a balloon, and the engine and the offer carry on, so the user can try again.
///
/// Installing needs an elevated tray: only an administrator can write the updates folder, which is what keeps the
/// installer from being swapped between the check and msiexec. An unelevated tray (started by hand) still checks, and
/// its offer opens the release's page in the browser instead, to download and run the installer from there, as UAC
/// would ask anyway. A build with the placeholder release key can't verify anything, so it doesn't check: "Check for
/// updates" points to the releases page.
sealed partial class TrayModel
{
    UpdateOffer? _installing; // the offer InstallUpdate is downloading or installing, one at a time

    /// Whether an offer can be installed from here, rather than downloaded from its page.
    bool CanInstall => _engine.IsElevated && _updates.HasReleaseKey;

    /// The offer being installed, from its download until its installer exits; null otherwise.
    UpdateOffer? Installing => Volatile.Read(ref _installing);

    /// "Check for updates": checks now, even with `updates: off` (the user asked), and says what it found: an offer,
    /// that this is the latest version, or why it couldn't tell.
    public async Task CheckForUpdates()
    {
        if (!_updates.HasReleaseKey)
        {
            _host.ShowBalloon(Strings.NoReleaseKeyTitle, Strings.NoReleaseKey(ReleaseFeed.ReleasesPage), isError: false,
                onClick: () => _ = OpenPage(ReleaseFeed.ReleasesPage));
            return;
        }

        UpdateCheckResult result;
        try
        {
            result = await _updates.CheckNowAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // the tray is quitting
        }

        if (result.Offer is { } offer) ShowOffer(offer);
        else if (result.Problem is { } problem) _host.ShowBalloon(Strings.UpdateCheckFailedTitle, Cut(problem, BalloonTextLimit), isError: true);
        else _host.ShowBalloon(Strings.UpToDateTitle, Strings.UpToDate(result.Running), isError: false);
    }

    /// Installs `offer`: downloads and verifies it (the engine runs on meanwhile), then, on the worker, pauses the
    /// engine, which darkens the screen and the ring, starts the installer, and waits for it, paused, with the installer
    /// held open. Paused rather than stopped, so that an installer that doesn't start or doesn't install gives the screen
    /// back (unless the user had paused it). The installer has the tray quit before it replaces its files, and Quit
    /// (which disposes the updates) ends the wait. Unelevated, opens the release's page instead. Quit during the download
    /// cancels it, quietly. One at a time: a click while one is under way does nothing.
    public async Task InstallUpdate(UpdateOffer offer)
    {
        if (!CanInstall)
        {
            await OpenPage((offer.ReleasePage?.AbsoluteUri) ?? ReleaseFeed.ReleasesPage).ConfigureAwait(false);
            return;
        }

        if (Interlocked.CompareExchange(ref _installing, offer, null) is not null) return;
        try
        {
            await DownloadAndInstall(offer).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _installing, null);
        }
    }

    async Task DownloadAndInstall(UpdateOffer offer)
    {
        VerifiedUpdate? update;
        try
        {
            update = await _updates.DownloadAsync(offer).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            // UpdateException (refused or failed), or the folder can't be made safe; anything else is a bug, shown the
            // same way rather than lost with the task nobody awaits.
            UpdateFailed(ex);
            return;
        }

        if (update is null) return; // a click while it was already downloading

        var wasPaused = false;
        IStartedProcess? installer = null;
        await Run(() =>
        {
            wasPaused = _engine.IsPaused;
            _engine.Pause();
            try
            {
                installer = update.Install();
            }
            catch (UpdateException ex)
            {
                if (!wasPaused) _engine.Resume();
                UpdateFailed(ex);
            }
        }).ConfigureAwait(false);

        int exitCode;
        try
        {
            if (installer is null) return; // it didn't start, and has said why
            exitCode = await _updates.WaitForInstallerAsync(installer).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // the tray is quitting: the installer asked it to, or the user did
        }
        catch (Exception ex)
        {
            // Waiting for a process doesn't fail; if it ever did, the screen must not stay dark.
            await GiveScreenBack(wasPaused).ConfigureAwait(false);
            UpdateFailed(ex);
            return;
        }
        finally
        {
            // Let go of the installer, so that trying again can replace it.
            installer?.Dispose();
            update.Dispose();
        }

        // It exited without asking the tray to quit, so it didn't install: the screen comes back.
        await GiveScreenBack(wasPaused).ConfigureAwait(false);
        if (VerifiedUpdate.Installed(exitCode))
        {
            _log.Info(Strings.InstallerFinishedWithoutQuit(exitCode));
            return;
        }

        var message = Strings.UpdateDidNotInstall(exitCode, update.LogPath);
        _log.Error(message);
        _host.ShowBalloon(Strings.UpdateFailedTitle, Cut(message, BalloonTextLimit), isError: true);
    }

    /// Resumes the engine after an update that didn't install, unless the user had paused it before.
    Task GiveScreenBack(bool wasPaused) => Run(() =>
    {
        if (!wasPaused) _engine.Resume();
    });

    /// "Install Marquee <version>…" (or "Download…" where it opens the page) while there is an offer, disabled while it
    /// downloads or installs; "Check for updates" otherwise.
    TrayMenuItem UpdateItem()
    {
        if (_updates.Downloading is { } download)
            return new TrayMenuItem(MenuKeys.InstallUpdate, OfferItemText(download.Offer)) { Enabled = false };
        if (Installing is { } installing)
            return new TrayMenuItem(MenuKeys.InstallUpdate, OfferItemText(installing)) { Enabled = false };
        if (_updates.Offer is { } offer)
            return new TrayMenuItem(MenuKeys.InstallUpdate, OfferItemText(offer)) { Command = () => _ = InstallUpdate(offer) };
        return new TrayMenuItem(MenuKeys.CheckForUpdates, Strings.MenuCheckForUpdates) { Command = () => _ = CheckForUpdates() };
    }

    string OfferItemText(UpdateOffer offer) =>
        CanInstall ? Strings.MenuInstallUpdate(VersionOf(offer)) : Strings.MenuDownloadUpdate(VersionOf(offer));

    /// The offer's balloon: a click installs it (or opens its page). From the daily check's thread or a command's.
    ///
    /// Any program running as the user can fake that click: Windows lets the shell's balloon messages through to the
    /// elevated tray's window, and they can't be told from a real click. It is accepted: a click can only install the
    /// offered release, which the tray already offers to anyone at its menu, and only once its signature has verified.
    void ShowOffer(UpdateOffer offer) =>
        _host.ShowBalloon(Strings.UpdateAvailableTitle,
            CanInstall ? Strings.UpdateAvailable(VersionOf(offer)) : Strings.UpdateAvailableDownload(VersionOf(offer)),
            isError: false, onClick: () => _ = InstallUpdate(offer));

    /// UpdateExceptions are in the log already (the installer logs them); the folder's errors aren't.
    void UpdateFailed(Exception ex)
    {
        if (ex is not UpdateException) _log.Error(Strings.UpdateFailed(ex.Message));
        _host.ShowBalloon(Strings.UpdateFailedTitle, Cut(ex.Message, BalloonTextLimit), isError: true);
    }

    /// A web page, through Explorer, which opens it in the user's browser, unelevated.
    Task OpenPage(string url) => Run(() => _shell.Open(url));

    static string VersionOf(UpdateOffer offer) => offer.Version.ToString(3);
}
