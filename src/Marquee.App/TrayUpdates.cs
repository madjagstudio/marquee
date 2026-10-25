using Marquee.App.Resources;
using Marquee.Core;
using Marquee.Core.Files;
using Marquee.Core.Logging;
using Marquee.Host.Updates;

namespace Marquee.App;

/// The tray's self-update, with no UI (TrayModel shows it): the daily check, the offer it found, and downloading that
/// offer into the admin-only updates folder. The check runs on its own timer: it waits UpdateSchedule.NextCheckIn,
/// checks, records the check whether it worked or not (so a GitHub it can't reach is asked again the next day, not in
/// a loop), and waits again. `updates: off` makes the wait endless; SetEnabled, at start and after every config.yaml
/// edit, turns it on and off. While the placeholder release key is built in, nothing is checked on schedule, since
/// nothing found could be installed.
///
/// `stateFile` is UpdateSchedule.FileName in the tray's data folder, `downloads` is MarqueePaths.Updates, and
/// `secureFolder` makes that folder admin-only before each download. `now` and `delay` are the clock and Task.Delay, for
/// tests. Thread-safe; Offered is raised on a thread-pool thread.
sealed class TrayUpdates : IDisposable
{
    readonly UpdateChecker _checker;
    readonly UpdateInstaller _installer;
    readonly string _stateFile;
    readonly string _downloads;
    readonly Action<string> _secureFolder;
    readonly ILog _log;
    readonly Func<DateTimeOffset> _now;
    readonly Func<TimeSpan, CancellationToken, Task> _delay;
    readonly DateTimeOffset _startedAt;
    readonly CancellationTokenSource _stop = new(); // cancelled by Dispose: ends the timer, a check and a download
    readonly object _gate = new();
    UpdateSchedule? _schedule; // null until SetEnabled
    bool _enabled;
    CancellationTokenSource? _wake; // ends the timer's current wait, to ask the schedule again
    Task? _timer;
    VerifiedUpdate? _verified; // held while its installer runs: msiexec reads the file that was verified
    volatile UpdateOffer? _offer;
    volatile DownloadProgress? _downloading;
    bool _disposed;

    public TrayUpdates(UpdateChecker checker, UpdateInstaller installer, string stateFile, string downloads,
        Action<string> secureFolder, ILog log, Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _checker = checker;
        _installer = installer;
        _stateFile = stateFile;
        _downloads = downloads;
        _secureFolder = secureFolder;
        _log = log;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
        _startedAt = _now();
    }

    /// The real one: GitHub through `http` (UpdateHttp.CreateClient, one for the tray's lifetime), this Marquee's
    /// version and release key, the real msiexec, and %ProgramData%\Marquee\updates.
    public static TrayUpdates ForThisApp(HttpClient http, string dataFolder, ILog log) =>
        new(UpdateChecker.ForThisApp(http, log), UpdateInstaller.ForThisApp(http, log),
            Path.Combine(dataFolder, UpdateSchedule.FileName), MarqueePaths.Updates, SecureFolder.Ensure, log);

    /// A check on schedule found a release newer than the one offered so far (once per version).
    public event Action<UpdateOffer>? Offered;

    /// Whether this build can install updates at all: false while the placeholder release key is built in.
    public bool HasReleaseKey => _checker.HasReleaseKey;

    /// The newest release found, until the tray exits; null when none was.
    public UpdateOffer? Offer => _offer;

    /// The download under way, or null.
    public DownloadProgress? Downloading => _downloading;

    /// Turns the daily check on or off (config.yaml's `updates:`); the first call starts the timer. Repeating the
    /// current setting changes nothing, so a config.yaml edit to something else doesn't move the next check.
    public void SetEnabled(bool enabled)
    {
        enabled &= HasReleaseKey;
        lock (_gate)
        {
            if (_disposed || (_schedule is not null && enabled == _enabled)) return;
            _enabled = enabled;
            _schedule = new UpdateSchedule(_stateFile, enabled, _startedAt, _log);
            _wake?.Cancel();
            _timer ??= Task.Run(RunTimer);
        }
    }

    /// "Check for updates": checks now, whatever `updates:` says, and records it, which puts the daily check off for a
    /// day. An offer it finds becomes Offer, without Offered (the caller shows it). Throws OperationCanceledException
    /// once disposed.
    public async Task<UpdateCheckResult> CheckNowAsync()
    {
        UpdateSchedule schedule;
        lock (_gate) schedule = _schedule ?? new UpdateSchedule(_stateFile, false, _startedAt, _log);

        var (result, _) = await Check(schedule).ConfigureAwait(false);
        lock (_gate) _wake?.Cancel(); // the timer asks the schedule again
        return result;
    }

    /// Makes the updates folder admin-only, downloads `offer`'s installer and signature into it and verifies them,
    /// reporting progress in Downloading. The VerifiedUpdate is held until Dispose, or until the caller disposes it
    /// (once its installer has exited without installing, so that the next download can replace the file). Null when a
    /// download is under way already. Throws UpdateException (refused or failed, logged), IOException or
    /// UnauthorizedAccessException (the folder can't be made safe), or OperationCanceledException once disposed.
    public async Task<VerifiedUpdate?> DownloadAsync(UpdateOffer offer)
    {
        CancellationToken cancel;
        lock (_gate)
        {
            if (_disposed) throw new OperationCanceledException(_stop.Token);
            if (_downloading is not null) return null;
            _downloading = new DownloadProgress(offer, 0);
            cancel = _stop.Token;
        }

        try
        {
            var progress = new Percent(offer.Installer.Size, percent => _downloading = new DownloadProgress(offer, percent));
            // Off the caller's thread: making the folder safe and verifying the installer both take a while.
            var update = await Task.Run(() =>
            {
                _secureFolder(_downloads);
                return _installer.DownloadAsync(offer, _downloads, progress, cancel);
            }, cancel).ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposed)
                {
                    update.Dispose();
                    throw new OperationCanceledException(_stop.Token);
                }

                _verified?.Dispose();
                _verified = update;
            }

            return update;
        }
        finally
        {
            _downloading = null;
        }
    }

    /// Waits for an installer the tray started and returns its exit code. Throws OperationCanceledException once
    /// disposed: the tray is quitting, because the installer asked it to (as it does before it replaces the tray's
    /// files) or the user did.
    public Task<int> WaitForInstallerAsync(IStartedProcess installer) => installer.WaitForExitAsync(_stop.Token);

    /// Stops the timer, cancels a check, a download or the wait for an installer under way, and lets go of a verified
    /// installer. A started installer has the package by then: it asks the tray to quit only once it is installing.
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            _verified?.Dispose();
            _verified = null;
        }
    }

    /// On a thread-pool thread until Dispose: waits for the schedule, checks, and again.
    async Task RunTimer()
    {
        while (true)
        {
            UpdateSchedule schedule;
            CancellationToken wake;
            lock (_gate)
            {
                if (_disposed) return;
                schedule = _schedule!;
                _wake?.Dispose();
                _wake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                wake = _wake.Token;
            }

            try
            {
                await _delay(schedule.NextCheckIn(_now()), wake).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                continue; // updates turned on or off, a manual check, or Dispose: ask again
            }

            try
            {
                var (result, isNew) = await Check(schedule).ConfigureAwait(false);
                if (isNew) Offered?.Invoke(result.Offer!);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // The checker logs its own failures and doesn't throw; this is a bug, or a handler's, and must not
                // stop the timer.
                _log.Error(Strings.UpdateTimerFailed(ex.Message));
            }
        }
    }

    /// Checks and records the check, success or not (the checker has logged what it found). IsNew: the offer is newer
    /// than the one found before, and is Offer now.
    async Task<(UpdateCheckResult Result, bool IsNew)> Check(UpdateSchedule schedule)
    {
        UpdateCheckResult result;
        try
        {
            result = await _checker.FindAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The checker returns its failures rather than throwing, so this is a bug; recorded all the same, or the
            // timer, which logs it and carries on, would find the check due at once and check again, in a loop.
            schedule.RecordCheck(_now());
            throw;
        }

        schedule.RecordCheck(_now());
        if (result.Offer is not { } offer) return (result, false);

        lock (_gate)
        {
            if (_offer is { } current && offer.Version <= current.Version) return (result, false);
            _offer = offer;
            return (result, true);
        }
    }

    /// Bytes of the installer as a whole percentage, reported as the download writes them (synchronously, so the
    /// numbers never arrive out of order as Progress<T>'s may).
    sealed class Percent(long size, Action<int> report) : IProgress<long>
    {
        public void Report(long value) => report((int)Math.Clamp(value * 100 / Math.Max(size, 1), 0, 100));
    }
}

/// A download under way: what, and how much of the installer has arrived.
sealed record DownloadProgress(UpdateOffer Offer, int Percent);
