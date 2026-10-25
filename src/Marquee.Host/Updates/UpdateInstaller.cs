using System.Net;
using System.Security.Cryptography;
using System.Text;
using Marquee.Core.Logging;
using Marquee.Host.Resources;

namespace Marquee.Host.Updates;

/// Downloads an offered update and its signature into MarqueePaths.Updates, verifies them, and returns a
/// VerifiedUpdate for installation. A missing or bad signature, a file of the
/// wrong size, a failed download, or the placeholder key built in (ReleaseKey.IsPlaceholder) refuses the update with
/// UpdateException, logged, and leaves nothing behind to run.
///
/// The folder is one for the whole PC, so two users' elevated trays (fast user switching) that both install at once
/// share it: one's clean-up or download may delete or replace the other's files, and that one then fails with a
/// download or signature error, to be tried again. Each tray runs only the installer it holds open and has verified
/// (VerifiedUpdate); the folder's permissions are the lasting guarantee.
///
/// `releaseKey` is the key updates must be signed with, `runningVersion` the running Marquee's informational version
/// (for the User-Agent); ForThisApp passes the real ones and starts the real msiexec.
public sealed class UpdateInstaller(HttpClient http, string runningVersion, ECDsa releaseKey, IProcessStarter processes, ILog log)
{
    /// How long a download may go without a byte arriving before it is given up.
    static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    /// The installer for this Marquee: its own version, the built-in key, the real msiexec. `http` comes from
    /// UpdateHttp.CreateClient.
    public static UpdateInstaller ForThisApp(HttpClient http, ILog log) =>
        new(http, ReleaseVersion.EntryAssembly ?? "", ReleaseKey.Public, new ProcessStarter(), log);

    /// Downloads the offer's installer and signature into `folder`, an existing folder only administrators can write,
    /// after deleting what earlier downloads left there, and verifies them. `progress` hears how many bytes of the
    /// installer have arrived. Throws UpdateException (logged) when the update is refused or the download fails, and
    /// OperationCanceledException when `cancellationToken` is cancelled; either way the new files are deleted.
    public async Task<VerifiedUpdate> DownloadAsync(UpdateOffer offer, string folder, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        try
        {
            return await Download(offer, folder, progress, cancellationToken);
        }
        catch (UpdateException ex)
        {
            log.Error(ex.Message);
            throw;
        }
    }

    async Task<VerifiedUpdate> Download(UpdateOffer offer, string folder, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        // Before anything is fetched: with the placeholder key, no release could be verified.
        if (ReleaseKey.IsPlaceholderKey(releaseKey)) throw new UpdateException(Strings.UpdateRefusedDevelopmentKey);
        if (offer.Installer.Size is <= 0 or > ReleaseFeed.MaxInstallerBytes)
            throw new UpdateException(Strings.ReleaseFileSize(offer.Installer.Name, offer.Installer.Size, ReleaseFeed.MaxInstallerBytes));

        // The names on disk come from the version, never from the release's text, so no name can lead a write elsewhere.
        var installerPath = Path.Combine(folder, ReleaseFeed.InstallerName(offer.Version));
        var signaturePath = Path.Combine(folder, ReleaseFeed.SignatureName(offer.Version));
        try
        {
            // What earlier downloads left there; a file that can't be deleted (an installer an earlier VerifiedUpdate
            // still holds) stays until next time.
            UpdateDownloads.DeleteFiles(folder);
            // The signature first: it is small, and without it there is no point fetching the installer.
            var signature = await DownloadSignature(offer.Signature, signaturePath, cancellationToken);
            await DownloadInstaller(offer.Installer, installerPath, progress, cancellationToken);

            // Opened before it is verified and held until it is installed: what verifies is what runs.
            var held = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (!ReleaseSignature.Verify(held, signature, releaseKey))
                    throw new UpdateException(Strings.UpdateSignatureInvalid(offer.Installer.Name));
                held.Position = 0;
                log.Info(Strings.UpdateVerified(offer.Installer.Name));
                return new VerifiedUpdate(offer, installerPath, held, releaseKey, processes, log);
            }
            catch
            {
                held.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            DeleteQuietly(installerPath);
            DeleteQuietly(signaturePath);
            if (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new UpdateException(Strings.UpdateDownloadStalled(StallTimeout.TotalSeconds));
            if (ex is HttpRequestException or IOException or UnauthorizedAccessException)
                throw new UpdateException(Strings.UpdateDownloadFailed(ex.Message));
            throw;
        }
    }

    /// The signature's text, saved to `path` too.
    async Task<string> DownloadSignature(ReleaseAsset asset, string path, CancellationToken cancellationToken)
    {
        using var stall = StallTimer(cancellationToken);
        using var request = UpdateHttp.Get(asset.DownloadUrl, runningVersion, "application/octet-stream");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new UpdateException(Strings.UpdateSignatureMissing(asset.Name));
        if (!response.IsSuccessStatusCode) throw new UpdateException(Strings.UpdateDownloadHttpError(asset.Name, (int)response.StatusCode));

        var bytes = await UpdateHttp.ReadAtMost(response.Content, ReleaseFeed.MaxSignatureBytes, stall.Token)
            ?? throw new UpdateException(Strings.UpdateSignatureTooLarge(asset.Name, ReleaseFeed.MaxSignatureBytes));
        await Save(path, async file => await file.WriteAsync(bytes, cancellationToken));
        // A .sig is ASCII; anything else becomes '?' and fails to verify.
        return Encoding.ASCII.GetString(bytes);
    }

    /// Streams the installer to `path`, refusing it as soon as it is longer than the release said, or if it ends shorter.
    async Task DownloadInstaller(ReleaseAsset asset, string path, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var stall = StallTimer(cancellationToken);
        using var request = UpdateHttp.Get(asset.DownloadUrl, runningVersion, "application/octet-stream");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        if (!response.IsSuccessStatusCode) throw new UpdateException(Strings.UpdateDownloadHttpError(asset.Name, (int)response.StatusCode));
        if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
            throw new UpdateException(Strings.UpdateWrongSize(asset.Name, asset.Size));

        await using var body = await response.Content.ReadAsStreamAsync(stall.Token);
        await Save(path, async file =>
        {
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                stall.CancelAfter(StallTimeout);
                var read = await body.ReadAsync(buffer, stall.Token);
                if (read == 0) break;
                total += read;
                if (total > asset.Size) throw new UpdateException(Strings.UpdateWrongSize(asset.Name, asset.Size));
                await file.WriteAsync(buffer.AsMemory(0, read), stall.Token);
                progress?.Report(total);
            }

            if (total != asset.Size) throw new UpdateException(Strings.UpdateWrongSize(asset.Name, asset.Size));
        });
    }

    /// Cancelled when `cancellationToken` is, or StallTimeout after it was last set.
    static CancellationTokenSource StallTimer(CancellationToken cancellationToken)
    {
        var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(StallTimeout);
        return stall;
    }

    /// Writes a new file under a temporary name, then gives it `path`'s name, so `path` is never a part-written file.
    static async Task Save(string path, Func<FileStream, Task> write)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.partial";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await write(file);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            DeleteQuietly(temporary);
            throw;
        }
    }

    static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next download's clean-up.
        }
    }
}
