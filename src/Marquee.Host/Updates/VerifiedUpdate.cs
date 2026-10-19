using System.ComponentModel;
using System.Security.Cryptography;
using Marquee.Core.Logging;
using Marquee.Host.Resources;

namespace Marquee.Host.Updates;

/// A downloaded installer whose signature verified (UpdateInstaller), held open from before it was verified until this
/// is disposed: opened for reading and shared for reading only, so while it is held nothing can write to it, replace it,
/// rename it or delete it. msiexec only reads it, which the sharing allows.
///
/// What the held handle guarantees is that the file Windows Installer opens is the file that was verified, as long as
/// it is held until Windows Installer has the package: the tray holds it while msiexec runs, until the installer asks
/// the tray to quit (which it does only once it is installing) or msiexec exits. It guarantees nothing after that, nor
/// for a copy anyone else starts. The lasting guarantee is the folder: the updates folder is admin-only
/// (SecureFolder.Ensure), so no unelevated program can change what is in it at any time.
public sealed class VerifiedUpdate : IDisposable
{
    readonly FileStream held;
    readonly ECDsa releaseKey;
    readonly IProcessStarter processes;
    readonly ILog log;
    bool disposed;

    internal VerifiedUpdate(UpdateOffer offer, string installerPath, FileStream held, ECDsa releaseKey, IProcessStarter processes, ILog log)
    {
        Offer = offer;
        InstallerPath = installerPath;
        this.held = held;
        this.releaseKey = releaseKey;
        this.processes = processes;
        this.log = log;
    }

    public UpdateOffer Offer { get; }

    /// The installer's full path, in the folder it was downloaded to.
    public string InstallerPath { get; }

    /// Where msiexec writes its verbose log: beside the installer, named after it, so it shows what a failed or
    /// rolled-back update did, and the next download's clean-up deletes it with the rest ("Marquee-…").
    public string LogPath => LogPathFor(InstallerPath);

    /// msiexec by its full path in System32: an elevated Marquee must never start whatever a writable folder on the PATH
    /// calls msiexec.
    public static string MsiexecPath => Path.Combine(Environment.SystemDirectory, "msiexec.exe");

    /// msiexec's command line. /passive is Windows Installer's "progress bar only" mode: the user, who just clicked
    /// Install, sees the update happen and is asked nothing, not even to close a dialog at the end, while the tray waits,
    /// paused (the installer closes it before it replaces its files, and starts it again when it finishes). /qb, the basic
    /// UI, adds dialogs whose answers nobody needs. /norestart, because /passive would otherwise restart the PC without
    /// asking if Windows Installer thought it had to; Marquee's files are free once the tray has quit, so no restart
    /// should be needed. /l*v, because with no dialogs the log is the only record of why an update failed or rolled
    /// back.
    public static string MsiexecArguments(string installerPath) =>
        $"/i \"{installerPath}\" /passive /norestart /l*v \"{LogPathFor(installerPath)}\"";

    /// Whether msiexec's exit code means the update installed: 0, or 3010 and 1641, installed with a restart needed or
    /// started. Anything else (1602 cancelled, 1603 failed, 1618 another installation under way, ...) means it didn't,
    /// and Windows Installer has rolled back whatever it had begun.
    public static bool Installed(int exitCode) => exitCode is 0 or 3010 or 1641;

    /// Starts the installer and returns it, to wait for, at once. Keep this held while it runs (see above); the
    /// installer then closes the tray, which disposes this, so the installer can replace MarqueeTray.exe. Throws
    /// UpdateException (logged) when the placeholder key is built in or msiexec can't be started, and
    /// ObjectDisposedException after Dispose.
    public IStartedProcess Install()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        // Checked again here, as close to msiexec as it can be (DownloadAsync checked first).
        if (ReleaseKey.IsPlaceholderKey(releaseKey)) throw Refuse(Strings.UpdateRefusedDevelopmentKey);

        log.Info(Strings.InstallerStarting(Offer.Version.ToString(3)));
        try
        {
            return processes.Start(MsiexecPath, MsiexecArguments(InstallerPath));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw Refuse(Strings.InstallerDidNotStart(ex.Message));
        }
    }

    public void Dispose()
    {
        disposed = true;
        held.Dispose();
    }

    UpdateException Refuse(string message)
    {
        log.Error(message);
        return new UpdateException(message);
    }

    static string LogPathFor(string installerPath) => installerPath + ".log";
}

/// The update was refused or failed: a missing or bad signature, the placeholder key, a download that failed. The
/// message says which, in words for the tray, and is already in the log.
public sealed class UpdateException(string message) : Exception(message);
