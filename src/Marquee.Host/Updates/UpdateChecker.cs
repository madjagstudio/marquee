using System.Net;
using System.Security.Cryptography;
using System.Text;
using Marquee.Core.Logging;
using Marquee.Host.Resources;

namespace Marquee.Host.Updates;

/// Looks for a newer Marquee: one unauthenticated GET of the latest release from GitHub's API (ReleaseFeed), which is
/// the only request Marquee makes on its own (the README says so). It sends the User-Agent GitHub requires,
/// "Marquee/<version>", and nothing else. Whatever goes wrong (no network, no answer within the timeout, GitHub's limit
/// of 60 requests an hour from one address without an account, no release yet, a release it can't use) is one line in
/// the log and no offer, and the tray asks again the next day (UpdateSchedule). With the placeholder key built in
/// (ReleaseKey.IsPlaceholder) it doesn't ask at all, since nothing it found could be installed.
///
/// `runningVersion` is the running Marquee's informational version (ReleaseVersion), `releaseKey` the key updates must
/// be signed with; ForThisApp passes the real ones.
public sealed class UpdateChecker(HttpClient http, string runningVersion, ECDsa releaseKey, ILog log, TimeSpan timeout)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// A release's JSON is a few KB, plus its notes; an answer over this isn't read.
    const int MaxAnswerBytes = 1024 * 1024;

    /// The checker for this Marquee: its own version, the built-in key, the default timeout. `http` comes from
    /// UpdateHttp.CreateClient.
    public static UpdateChecker ForThisApp(HttpClient http, ILog log) =>
        new(http, ReleaseVersion.EntryAssembly ?? "", ReleaseKey.Public, log, DefaultTimeout);

    /// The latest release when it is newer than this Marquee, or null; either way logged, once. Throws
    /// OperationCanceledException only when `cancellationToken` is cancelled.
    public async Task<UpdateOffer?> CheckAsync(CancellationToken cancellationToken)
    {
        if (ReleaseKey.IsPlaceholderKey(releaseKey))
        {
            log.Info(Strings.UpdateCheckSkippedDevelopmentKey);
            return null;
        }

        if (!ReleaseVersion.TryParseRunning(runningVersion, out var running))
        {
            log.Warn(Strings.UpdateCheckSkippedNoVersion(runningVersion));
            return null;
        }

        if (await FetchAsync(cancellationToken) is not { } json) return null;

        if (!ReleaseFeed.TryRead(json, out var release, out var problem))
        {
            log.Warn(Strings.UpdateCheckUnusableRelease(problem));
            return null;
        }

        if (!running.IsOlderThan(release.Version))
        {
            log.Info(Strings.UpToDate(running.ToString()));
            return null;
        }

        log.Info(Strings.UpdateAvailable(release.Version.ToString(3), running.ToString()));
        return release;
    }

    /// The answer's text, or null with the reason logged.
    async Task<string?> FetchAsync(CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            using var request = UpdateHttp.Get(new Uri(ReleaseFeed.LatestReleaseUrl), runningVersion, "application/vnd.github+json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timer.Token);
            if (!response.IsSuccessStatusCode)
            {
                LogRefusal(response);
                return null;
            }

            if (await UpdateHttp.ReadAtMost(response.Content, MaxAnswerBytes, timer.Token) is not { } bytes)
            {
                log.Warn(Strings.UpdateCheckAnswerTooLarge(MaxAnswerBytes));
                return null;
            }

            // GitHub's API always answers in UTF-8.
            return Encoding.UTF8.GetString(bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log.Warn(Strings.UpdateCheckTimedOut(timeout.TotalSeconds));
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            log.Warn(Strings.UpdateCheckFailed(ex.Message));
            return null;
        }
    }

    void LogRefusal(HttpResponseMessage response)
    {
        // GitHub answers 404 for a repository with no published release, and 403 or 429 when the address has no requests
        // left in the hour's allowance (X-RateLimit-Remaining: 0) or is asked to slow down (Retry-After). Neither is a
        // fault, so neither is a warning.
        if (response.StatusCode == HttpStatusCode.NotFound)
            log.Info(Strings.NoReleaseYet);
        else if (response.StatusCode == HttpStatusCode.TooManyRequests
            || (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response)))
            log.Info(Strings.UpdateCheckRateLimited);
        else
            log.Warn(Strings.UpdateCheckHttpError((int)response.StatusCode, response.ReasonPhrase ?? ""));
    }

    static bool IsRateLimited(HttpResponseMessage response) =>
        response.Headers.RetryAfter is not null
        || (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault()?.Trim() == "0");
}
